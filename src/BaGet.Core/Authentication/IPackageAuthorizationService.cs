using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

public enum PackageAction
{
    /// <summary>
    /// Push a package or a symbol package.
    /// </summary>
    Push,

    /// <summary>
    /// Unlist, relist or delete a version (the access model's <see cref="AccessAction.Delete"/>).
    /// </summary>
    Manage,

    /// <summary>
    /// Edit a version's deprecation or vulnerabilities (<see cref="AccessAction.Annotate"/>). Needs ownership and the
    /// unlist scope, like <see cref="Manage"/>.
    /// </summary>
    Annotate,
}

/// <summary>
/// Decides whether an authenticated principal may act on a package ID.
/// </summary>
public interface IPackageAuthorizationService
{
    Task<bool> AuthorizeAsync(
        BaGetPrincipal? principal,
        PackageAction action,
        string? packageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Called after a successful push, so that a user pushing a new package ID becomes
    /// its owner.
    /// </summary>
    /// <param name="principal">The pusher.</param>
    /// <param name="packageId">The pushed ID.</param>
    /// <param name="isNewId">
    /// The ID had no version before this push, decided by the caller before submitting. Pushing a
    /// version of an existing ID never claims it.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnPushedAsync(BaGetPrincipal principal, string? packageId, bool isNewId, CancellationToken cancellationToken);
}

/// <summary>
/// Without accounts every authenticated caller is the superuser, as it always was.
/// </summary>
public class AllowAuthenticatedAuthorizationService : IPackageAuthorizationService
{
    public Task<bool> AuthorizeAsync(
        BaGetPrincipal? principal,
        PackageAction action,
        string? packageId,
        CancellationToken cancellationToken)
        => Task.FromResult(principal != null);

    public Task OnPushedAsync(BaGetPrincipal principal, string? packageId, bool isNewId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}