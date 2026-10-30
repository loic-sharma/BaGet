using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>One member's answer to a question asked of every member of a virtual repository.</summary>
/// <typeparam name="T">The answer.</typeparam>
/// <param name="Member">The member that answered.</param>
/// <param name="Value">What it answered.</param>
public sealed record MemberResult<T>(RepositoryInfo Member, T Value);

/// <summary>
/// The engine behind reading a <see cref="RepositoryKind.Virtual"/> repository. It finds the members the caller may
/// read, in resolution order, and runs a question in each one's own DI scope (so the member's database filter, storage
/// path, upstream and access checks apply exactly as they do when the member is read directly). A member that fails
/// (its upstream is down) is logged and skipped, so the next member can answer; cancellation and a closed maintenance
/// gate are never swallowed. URLs in the answers still point into the virtual repository: they are generated from the
/// request, not from the member's scope.
/// </summary>
public sealed class VirtualRepositoryReader
{
    private readonly IRepositoryContext _repository;
    private readonly IVirtualRepositories _virtuals;
    private readonly IAccessEvaluator _evaluator;
    private readonly IRequestCaller _caller;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<VirtualRepositoryReader> _logger;

    public VirtualRepositoryReader(
        IRepositoryContext repository,
        IVirtualRepositories virtuals,
        IAccessEvaluator evaluator,
        IRequestCaller caller,
        IServiceScopeFactory scopes,
        ILogger<VirtualRepositoryReader> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether the request's repository is a virtual one (otherwise nothing here applies).</summary>
    public bool IsVirtual => _repository.Current.Kind == RepositoryKind.Virtual;

    /// <summary>
    /// The members of the request's virtual repository the caller may read, in resolution order. With
    /// <paramref name="packageId"/> the Read grants' package patterns apply too.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryInfo>> ReadableMembersAsync(string? packageId, CancellationToken cancellationToken)
    {
        if (!IsVirtual) return new[] { _repository.Current };

        var members = await _virtuals.EffectiveMembersAsync(_repository.Current, cancellationToken).ConfigureAwait(false);
        var principal = await _caller.GetAsync(cancellationToken).ConfigureAwait(false);
        var readable = new List<RepositoryInfo>();

        foreach (var member in members)
        {
            if (await _evaluator.IsAllowedAsync(principal, member, packageId: null, AccessAction.Read, cancellationToken).ConfigureAwait(false)
                && (packageId == null
                    || await _evaluator.IsAllowedAsync(principal, member, packageId, AccessAction.Read, cancellationToken).ConfigureAwait(false)))
            {
                readable.Add(member);
            }
        }

        return readable;
    }

    /// <summary>
    /// Whether the caller may read <paramref name="packageId"/> in <paramref name="member"/> (the repository, and the package when
    /// given): what a merged listing asks per hit, since the members' own listings do not know the caller.
    /// </summary>
    public async Task<bool> CanReadInMemberAsync(RepositoryInfo member, string? packageId, CancellationToken cancellationToken)
    {
        var principal = await _caller.GetAsync(cancellationToken).ConfigureAwait(false);

        return await _evaluator.IsAllowedAsync(principal, member, packageId: null, AccessAction.Read, cancellationToken).ConfigureAwait(false)
            && (packageId == null
                || await _evaluator.IsAllowedAsync(principal, member, packageId, AccessAction.Read, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Runs <paramref name="action"/> in a scope pinned to <paramref name="member"/>.</summary>
    public async Task<T> InMemberAsync<T>(RepositoryInfo member, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _scopes.CreateRepositoryScope(member);

        return await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the readable members in order and returns the first answer that is not null; null when none has one.
    /// </summary>
    public async Task<T?> FirstAsync<T>(string? packageId, Func<IServiceProvider, Task<T?>> action, CancellationToken cancellationToken)
        where T : class
    {
        foreach (var member in await ReadableMembersAsync(packageId, cancellationToken).ConfigureAwait(false))
        {
            var answer = await TryAsync(member, action).ConfigureAwait(false);
            if (answer != null) return answer;
        }

        return null;
    }

    /// <summary>Asks every readable member, in order; a member that fails contributes nothing.</summary>
    public async Task<IReadOnlyList<MemberResult<T>>> AllAsync<T>(string? packageId, Func<IServiceProvider, Task<T>> action, CancellationToken cancellationToken)
        where T : class
    {
        var results = new List<MemberResult<T>>();
        foreach (var member in await ReadableMembersAsync(packageId, cancellationToken).ConfigureAwait(false))
        {
            var answer = await TryAsync(member, async sp => (T?)await action(sp).ConfigureAwait(false)).ConfigureAwait(false);
            if (answer != null) results.Add(new MemberResult<T>(member, answer));
        }

        return results;
    }

    private async Task<T?> TryAsync<T>(RepositoryInfo member, Func<IServiceProvider, Task<T?>> action) where T : class
    {
        try
        {
            return await InMemberAsync(member, action).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
        {
            _logger.LogWarning(e, "Member {Member} of virtual repository {Virtual} failed; the next member answers instead", member.Name, _repository.Current.Name);

            return null;
        }
    }

    /// <summary>
    /// The versions of a package stored in the readable members (no upstream is asked), each version once, from the
    /// first member that has it.
    /// </summary>
    public async Task<IReadOnlyList<Package>> FindPackagesAsync(string? id, bool includeUnlisted, CancellationToken cancellationToken)
    {
        var all = await AllAsync(
            id,
            sp => sp.GetRequiredService<IPackageDatabase>().FindAsync(id, includeUnlisted, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return Union(all.Select(r => r.Value));
    }

    /// <summary>One version stored in the first readable member that has it (no upstream is asked).</summary>
    public Task<Package?> FindPackageAsync(string? id, NuGetVersion version, bool includeUnlisted, CancellationToken cancellationToken)
        => FirstAsync(
            id,
            sp => sp.GetRequiredService<IPackageDatabase>().FindOrNullAsync(id, version, includeUnlisted, cancellationToken),
            cancellationToken);

    /// <summary>The vulnerabilities recorded in the readable members, each advisory once.</summary>
    public async Task<IReadOnlyList<PackageVulnerability>> GetAllVulnerabilitiesAsync(CancellationToken cancellationToken)
    {
        var all = await AllAsync(
            packageId: null,
            sp => sp.GetRequiredService<IPackageDatabase>().GetAllVulnerabilitiesAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PackageVulnerability>();
        foreach (var vulnerability in all.SelectMany(r => r.Value))
        {
            var key = $"{vulnerability.Package?.Id}|{vulnerability.Package?.NormalizedVersionString}|{vulnerability.AdvisoryUrl}|{vulnerability.Severity}";
            if (seen.Add(key)) result.Add(vulnerability);
        }

        return result;
    }

    /// <summary>Package lists from several members merged by version: the earliest list wins a version.</summary>
    internal static IReadOnlyList<Package> Union(IEnumerable<IReadOnlyList<Package>> lists)
    {
        var byVersion = new Dictionary<NuGetVersion, Package>();
        foreach (var package in lists.SelectMany(l => l))
        {
            byVersion.TryAdd(package.Version, package);
        }

        return byVersion.Values.ToList();
    }
}
