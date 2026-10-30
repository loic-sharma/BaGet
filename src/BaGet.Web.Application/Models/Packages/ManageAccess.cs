using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Web.Application.Models.Packages;

/// <summary>
/// Who is shown the owners and the link to the manage page on the npm package, Maven artifact and Docker image screens. The manage pages exist
/// where packages are stored (a local repository) and, for owners, only while accounts are on.
/// </summary>
public sealed class ManageAccess
{
    private readonly IRepositoryContext _repository;
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly IServiceProvider _services;

    /// <summary>Creates the service.</summary>
    public ManageAccess(IRepositoryContext repository, IOptions<AccountsOptions> accounts, IServiceProvider services)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// The owners of an npm package or Maven artifact (<c>group:artifact</c>) and whether <paramref name="user"/> may open its manage page (an owner,
    /// directly or through an organization, or an administrator, signed in). Empty and false in a remote or virtual repository and while accounts are off.
    /// </summary>
    public async Task<(IReadOnlyList<string> Owners, bool CanManage)> OwnersAndManageAsync(ClaimsPrincipal user, string name, CancellationToken cancellationToken)
    {
        if (_repository.Current.Kind != RepositoryKind.Local || !_accounts.Value.Enabled) return (Array.Empty<string>(), false);

        var ownership = _services.GetService<PackageOwnershipService>();
        if (ownership == null) return (Array.Empty<string>(), false);

        var owners = (await ownership.GetOwnersAsync(name, cancellationToken)).Select(o => o.OwnerName ?? string.Empty).ToList();

        return (owners, await CanOpenAsync(user, name, cancellationToken));
    }

    /// <summary>An owner (directly or through an organization) or an administrator, signed in.</summary>
    private async Task<bool> CanOpenAsync(ClaimsPrincipal user, string name, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true) return false;

        var sessions = _services.GetService<ISessionPrincipalFactory>();
        var principal = sessions == null ? null : await sessions.CreateAsync(user);
        if (principal == null) return false;
        if (principal.IsAdministrator) return true;

        var ownership = _services.GetService<PackageOwnershipService>();

        return ownership != null && await ownership.IsOwnerAsync(principal.UserId, principal.UserName, name, cancellationToken);
    }

    /// <summary>Whether <paramref name="user"/> is offered the Docker manage page: signed in and holding Delete on the image (the rule the registry applies to a delete).</summary>
    public async Task<bool> CanManageImageAsync(ClaimsPrincipal user, RepositoryInfo repository, string image, CancellationToken cancellationToken)
    {
        // The tools exist where images are pushed: only a local repository (the manage page of another kind is 404).
        if (user.Identity?.IsAuthenticated != true || repository.Kind != RepositoryKind.Local) return false;

        var sessions = _services.GetService<ISessionPrincipalFactory>();
        var principal = sessions == null ? null : await sessions.CreateAsync(user);
        if (principal == null) return false;

        return await _services.GetRequiredService<IOciAccess>().IsAllowedAsync(principal, repository, image, OciActions.Delete, cancellationToken);
    }
}
