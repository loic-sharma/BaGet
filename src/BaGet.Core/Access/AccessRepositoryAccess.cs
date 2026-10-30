using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Turns a signed-in browser session into a <see cref="BaGetPrincipal"/>. Implemented by the accounts feature; a host
/// without accounts has none, and every browser caller is anonymous.
/// </summary>
public interface ISessionPrincipalFactory
{
    /// <returns>Null when nobody is signed in or the account no longer exists.</returns>
    Task<BaGetPrincipal?> CreateAsync(ClaimsPrincipal? user);
}

/// <summary>
/// <see cref="IRepositoryAccess"/> over the access model: the web UI offers, and opens, the repositories the
/// signed-in user (or an anonymous visitor) may read through <see cref="IAccessEvaluator"/>. Replaces
/// <see cref="OpenRepositoryAccess"/>.
/// </summary>
public sealed class AccessRepositoryAccess : IRepositoryAccess
{
    private readonly IAccessEvaluator _evaluator;
    private readonly IRepositoryService _repositories;
    private readonly ISessionPrincipalFactory? _sessions;
    private readonly IVirtualRepositories? _virtuals;

    public AccessRepositoryAccess(
        IAccessEvaluator evaluator,
        IRepositoryService repositories,
        ISessionPrincipalFactory? sessions = null,
        IVirtualRepositories? virtuals = null)
    {
        _virtuals = virtuals;
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _sessions = sessions;
    }

    /// <inheritdoc/>
    public bool SupportsMultipleRepositories => _repositories.SupportsMultipleRepositories;

    private async Task<BaGetPrincipal?> PrincipalAsync(ClaimsPrincipal? user)
        => _sessions == null ? null : await _sessions.CreateAsync(user).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepositoryInfo>> ReadableAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
        => await _evaluator.ReadableRepositoriesAsync(await PrincipalAsync(user).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The repositories the signed-in user holds Deploy in. A visitor who is not signed in may still push with an
    /// API key typed into the upload page, which is authorized per package when submitted, so for them every
    /// readable repository is offered.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryInfo>> WritableAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
    {
        var principal = await PrincipalAsync(user).ConfigureAwait(false);

        // A remote repository takes no uploads, whatever the caller holds.
        var writable = principal == null
            ? (await _evaluator.ReadableRepositoriesAsync(null, cancellationToken).ConfigureAwait(false))
                .Where(r => r.Kind == RepositoryKind.Local).ToList()
            : (await _evaluator.RepositoriesWithAsync(principal, AccessAction.Deploy, cancellationToken).ConfigureAwait(false)).ToList();

        // A virtual repository takes uploads when the caller can read it and write to the repository it deploys to.
        if (_virtuals != null)
        {
            var targets = writable.Select(r => r.Key).ToHashSet();
            foreach (var candidate in await _evaluator.ReadableRepositoriesAsync(principal, cancellationToken).ConfigureAwait(false))
            {
                if (candidate.Kind != RepositoryKind.Virtual) continue;

                var target = await _virtuals.DeployTargetAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (target != null && targets.Contains(target.Key)) writable.Add(candidate);
            }
        }

        return writable
            .OrderBy(r => r.IsDefault ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A signed-in user needs Deploy on the repository (Read is not required: a drop box can be pushed to); a visitor
    /// who is not signed in needs Read, as <see cref="WritableAsync"/> lists.
    /// </summary>
    public async Task<bool> CanWriteAsync(ClaimsPrincipal? user, RepositoryInfo repository, CancellationToken cancellationToken)
    {
        if (repository.Kind == RepositoryKind.Remote) return false;

        // A virtual repository's uploads go to its deploy repository: the caller needs to read the virtual one and to
        // write to that.
        if (repository.Kind == RepositoryKind.Virtual)
        {
            var target = _virtuals == null ? null : await _virtuals.DeployTargetAsync(repository, cancellationToken).ConfigureAwait(false);

            return target != null
                && await CanReadAsync(user, repository, cancellationToken).ConfigureAwait(false)
                && await CanWriteAsync(user, target, cancellationToken).ConfigureAwait(false);
        }

        var principal = await PrincipalAsync(user).ConfigureAwait(false);

        return principal == null
            ? await CanReadAsync(user, repository, cancellationToken).ConfigureAwait(false)
            : await _evaluator.IsAllowedAsync(principal, repository, packageId: null, AccessAction.Deploy, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> CanReadAsync(ClaimsPrincipal? user, RepositoryInfo repository, CancellationToken cancellationToken)
        => await _evaluator.IsAllowedAsync(
               await PrincipalAsync(user).ConfigureAwait(false), repository, packageId: null, AccessAction.Read, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<bool> CanReadPackageAsync(ClaimsPrincipal? user, RepositoryInfo repository, string packageId, CancellationToken cancellationToken)
    {
        var principal = await PrincipalAsync(user).ConfigureAwait(false);

        return await _evaluator.IsAllowedAsync(principal, repository, packageId: null, AccessAction.Read, cancellationToken).ConfigureAwait(false)
            && await _evaluator.IsAllowedAsync(principal, repository, packageId, AccessAction.Read, cancellationToken).ConfigureAwait(false);
    }
}
