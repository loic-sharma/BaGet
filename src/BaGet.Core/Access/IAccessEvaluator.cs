using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Decides what a caller may do in a repository, from the access model (groups, permission targets, project
/// roles). The first of two requirements for a write: package ownership (<see cref="IPackageAuthorizationService"/>)
/// is the second and still applies inside Deploy, Delete and Annotate.
/// </summary>
public interface IAccessEvaluator
{
    /// <summary>
    /// Whether <paramref name="principal"/> holds <paramref name="action"/> in <paramref name="repository"/>, on
    /// <paramref name="packageId"/> when given (the targets' include and exclude patterns apply), else on the
    /// repository as a whole (some package may match). A null principal is an anonymous caller.
    /// </summary>
    /// <remarks>
    /// A principal whose API key is limited to other repositories (<see cref="BaGetPrincipal.RepositoryKeys"/>) holds no
    /// action here, not even as an administrator, and reads as an anonymous caller.
    /// The server's superuser (global API key) and administrators (D5 of the access plan) hold every action. Where the
    /// database is not relational (Azure Table) there is no access model: reading is open and any authenticated caller
    /// holds the write actions, as before the model existed.
    /// </remarks>
    Task<bool> IsAllowedAsync(
        BaGetPrincipal? principal,
        RepositoryInfo repository,
        string? packageId,
        AccessAction action,
        CancellationToken cancellationToken);

    /// <summary>
    /// The NuGet repositories <paramref name="principal"/> may read, ordered by name (<c>default</c> first).
    /// </summary>
    Task<IReadOnlyList<RepositoryInfo>> ReadableRepositoriesAsync(BaGetPrincipal? principal, CancellationToken cancellationToken);

    /// <summary>
    /// The NuGet repositories in which <paramref name="principal"/> holds <paramref name="action"/>, ordered like
    /// <see cref="ReadableRepositoriesAsync"/>.
    /// </summary>
    Task<IReadOnlyList<RepositoryInfo>> RepositoriesWithAsync(BaGetPrincipal? principal, AccessAction action, CancellationToken cancellationToken);

    /// <summary>
    /// What <paramref name="principal"/> may do in <paramref name="repository"/> (on <paramref name="packageId"/> when
    /// given) and which grants and project roles say so: the same decision as <see cref="IsAllowedAsync"/>, with its
    /// reasons, for the admin and account pages.
    /// </summary>
    Task<AccessExplanation> ExplainAsync(
        BaGetPrincipal? principal,
        RepositoryInfo repository,
        string? packageId,
        CancellationToken cancellationToken);

    /// <summary>Who holds what in <paramref name="repository"/>, whatever the caller (the repository page's view).</summary>
    Task<RepositoryAccessSummary> DescribeAsync(RepositoryInfo repository, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IAccessEvaluator"/> over the relational database, through the cached <see cref="AccessSnapshot"/>.
/// </summary>
public sealed class AccessEvaluator : IAccessEvaluator
{
    private readonly IContext _context;
    private readonly AccessModelCache _cache;
    private readonly IRepositoryService _repositories;

    public AccessEvaluator(IContext context, AccessModelCache cache, IRepositoryService repositories)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    /// <inheritdoc/>
    public async Task<bool> IsAllowedAsync(
        BaGetPrincipal? principal,
        RepositoryInfo repository,
        string? packageId,
        AccessAction action,
        CancellationToken cancellationToken)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        if (action == AccessAction.None) return true;

        // Nothing is deployed to a remote repository, whoever asks (an administrator included): it holds what the
        // server itself cached from its upstream. Reading, deleting cached packages and the rest follow the model.
        if (repository.Kind == RepositoryKind.Remote && (action & AccessAction.Deploy) != 0) return false;

        // Nor to a virtual repository: a push to it goes to its deploy repository (the request is redirected there before
        // anything is authorized), and Deploy is judged on that one.
        if (repository.Kind == RepositoryKind.Virtual && (action & AccessAction.Deploy) != 0) return false;

        // An API key limited to some repositories (ApiKeyRepository) is not a credential in the others: it grants
        // nothing there, and a read is judged as the anonymous caller's, so a key never sees more than no key at all.
        if (principal != null && !principal.AllowsRepository(repository.Key))
        {
            if (action != AccessAction.Read) return false;
            principal = null;
        }

        if (_context is NullContext) return action == AccessAction.Read || principal != null;
        if (principal != null && (principal.IsSuperuser || principal.IsAdministrator)) return true;

        var snapshot = await _cache.GetAsync(_context, cancellationToken).ConfigureAwait(false);
        var membership = await MembershipAsync(snapshot, principal, cancellationToken).ConfigureAwait(false);

        return (snapshot.Granted(principal, membership, repository, packageId) & action) == action;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RepositoryInfo>> ReadableRepositoriesAsync(BaGetPrincipal? principal, CancellationToken cancellationToken)
        => RepositoriesWithAsync(principal, AccessAction.Read, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepositoryInfo>> RepositoriesWithAsync(
        BaGetPrincipal? principal,
        AccessAction action,
        CancellationToken cancellationToken)
    {
        var all = await _repositories.ListAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<RepositoryInfo>();

        foreach (var repository in all)
        {
            if (await IsAllowedAsync(principal, repository, packageId: null, action, cancellationToken).ConfigureAwait(false))
            {
                result.Add(repository);
            }
        }

        return result
            .OrderBy(r => r.IsDefault ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<AccessExplanation> ExplainAsync(
        BaGetPrincipal? principal,
        RepositoryInfo repository,
        string? packageId,
        CancellationToken cancellationToken)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));

        var explanation = await ExplainCoreAsync(principal, repository, packageId, cancellationToken).ConfigureAwait(false);

        // Mirrors IsAllowedAsync: Deploy is never held in a remote or a virtual repository.
        return repository.Kind == RepositoryKind.Remote || repository.Kind == RepositoryKind.Virtual
            ? explanation with { Actions = explanation.Actions & ~AccessAction.Deploy }
            : explanation;
    }

    private async Task<AccessExplanation> ExplainCoreAsync(
        BaGetPrincipal? principal,
        RepositoryInfo repository,
        string? packageId,
        CancellationToken cancellationToken)
    {

        if (_context is NullContext)
        {
            return new AccessExplanation(
                principal != null ? AccessAction.All : AccessAction.Read,
                "This database has no access model: everyone reads and any authenticated caller writes, subject to package ownership.",
                Array.Empty<AccessReason>());
        }

        if (principal != null && principal.IsSuperuser)
        {
            return new AccessExplanation(AccessAction.All, "The server's global API key holds every action everywhere.", Array.Empty<AccessReason>());
        }

        if (principal != null && principal.IsAdministrator)
        {
            return new AccessExplanation(AccessAction.All, "Server administrators hold every action everywhere.", Array.Empty<AccessReason>());
        }

        var snapshot = await _cache.GetAsync(_context, cancellationToken).ConfigureAwait(false);
        var membership = await MembershipAsync(snapshot, principal, cancellationToken).ConfigureAwait(false);
        var reasons = new List<AccessReason>();
        var actions = snapshot.Granted(principal, membership, repository, packageId, reasons);

        return new AccessExplanation(actions, null, reasons);
    }

    /// <inheritdoc/>
    public async Task<RepositoryAccessSummary> DescribeAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        if (_context is NullContext) return new RepositoryAccessSummary(repository, AnonymousRead: true, Project: null, ModelAvailable: false, Array.Empty<AccessReason>());

        var snapshot = await _cache.GetAsync(_context, cancellationToken).ConfigureAwait(false);
        snapshot.Repositories.TryGetValue(repository.Key, out var row);
        var holders = snapshot.Holders(repository);

        var userIds = holders.Where(h => h.PrincipalType == PrincipalType.User).Select(h => h.PrincipalId).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var names = await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName })
                .ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken).ConfigureAwait(false);
            holders = holders
                .Select(h => h.PrincipalType == PrincipalType.User && names.TryGetValue(h.PrincipalId, out var name)
                    ? h with { Principal = "user " + name }
                    : h)
                .ToList();
        }

        return new RepositoryAccessSummary(
            repository,
            row?.AnonymousRead ?? true,
            row?.ProjectKey != null && snapshot.ProjectNames.TryGetValue(row.ProjectKey.Value, out var project) ? project : null,
            ModelAvailable: true,
            holders);
    }

    private Task<Membership> MembershipAsync(AccessSnapshot snapshot, BaGetPrincipal? principal, CancellationToken cancellationToken)
        => principal?.UserId == null
            ? Task.FromResult(Membership.None)
            : snapshot.MembershipAsync(_context, principal.UserId, cancellationToken);
}
