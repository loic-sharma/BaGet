using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Identity.Application.Security;

/// <summary>
/// Whether a signed-in user holds an access model action on a package in the current repository: the permission half of
/// the Manage page's rule (<see cref="AccessAction.Manage"/> for owners, <see cref="AccessAction.Delete"/> for unlisting and
/// deleting, <see cref="AccessAction.Annotate"/> for deprecation and vulnerabilities, as the protocol endpoints ask). Ownership is the other half (D4 of the access plan: a
/// permission is necessary, ownership remains necessary); administrators hold every action.
/// </summary>
public static class ManagePermission
{
    /// <param name="services">The request's services.</param>
    /// <param name="user">The signed-in user.</param>
    /// <param name="packageId">The package ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static Task<bool> HeldAsync(IServiceProvider services, ClaimsPrincipal user, string? packageId, CancellationToken cancellationToken)
        => HeldAsync(services, user, packageId, AccessAction.Manage, cancellationToken);

    /// <summary>Whether the user holds any of the actions the Manage page's handlers need (enough to open the page and see its link).</summary>
    /// <param name="services">The request's services.</param>
    /// <param name="user">The signed-in user.</param>
    /// <param name="packageId">The package ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<bool> HeldAnyAsync(IServiceProvider services, ClaimsPrincipal user, string? packageId, CancellationToken cancellationToken)
    {
        foreach (var action in new[] { AccessAction.Manage, AccessAction.Delete, AccessAction.Annotate })
        {
            if (await HeldAsync(services, user, packageId, action, cancellationToken).ConfigureAwait(false)) return true;
        }

        return false;
    }

    /// <summary>Whether the user holds <paramref name="action"/> on the package.</summary>
    /// <param name="services">The request's services.</param>
    /// <param name="user">The signed-in user.</param>
    /// <param name="packageId">The package ID.</param>
    /// <param name="action">The access model action.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<bool> HeldAsync(IServiceProvider services, ClaimsPrincipal user, string? packageId, AccessAction action, CancellationToken cancellationToken)
    {
        var sessions = services.GetService<ISessionPrincipalFactory>();
        var evaluator = services.GetService<IAccessEvaluator>();
        if (sessions == null || evaluator == null) return true;

        var principal = await sessions.CreateAsync(user).ConfigureAwait(false);
        var repository = services.GetRequiredService<IRepositoryContext>().Current;

        return await evaluator.IsAllowedAsync(principal, repository, packageId, action, cancellationToken).ConfigureAwait(false);
    }
}
