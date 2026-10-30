using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BaGet.Identity.Application.Services.Ownership;

/// <summary>
/// The rules for changing packages, in two layers. First the access model (<see cref="IAccessEvaluator"/>): the caller must hold the matching action (Deploy to push, Delete to unlist or delete, Annotate to edit deprecation and
/// vulnerabilities) on the package in the current repository. Then package ownership, as nuget.org applies it:
/// <list type="bullet">
/// <item>The superuser may do anything.</item>
/// <item>A key acts only within its scopes and its package glob.</item>
/// <item>An administrator skips ownership, never the key's own limits (scopes and glob): a
/// push-only key an administrator made for CI stays push-only. It may push a version of,
/// or unlist, any existing ID, ownerless ones included; a new ID still needs
/// <see cref="ApiKeyScopes.Push"/>.</item>
/// <item>Pushing a new package ID needs <see cref="ApiKeyScopes.Push"/>, and makes the
/// pusher its owner.</item>
/// <item>Anything else needs the user to own the ID, directly or through an
/// organization.</item>
/// <item>An ID that already exists but has no owners predates accounts, and only the
/// superuser may change it, so nothing becomes inaccessible on upgrade.</item>
/// </list>
/// </summary>
public class OwnershipAuthorizationService : IPackageAuthorizationService
{
    private readonly PackageOwnershipService _ownership;
    private readonly IPackageDatabase _packages;
    private readonly IContext? _context;
    private readonly ILogger<OwnershipAuthorizationService> _logger;
    private readonly IAccessEvaluator _access;
    private readonly IRepositoryContext _repository;

    public OwnershipAuthorizationService(
        PackageOwnershipService ownership,
        IPackageDatabase packages,
        ILogger<OwnershipAuthorizationService> logger,
        IAccessEvaluator access,
        IRepositoryContext repository,
        IContext? context = null)
    {
        _context = context;
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> AuthorizeAsync(
        BaGetPrincipal? principal,
        PackageAction action,
        string? packageId,
        CancellationToken cancellationToken)
    {
        if (principal == null) return false;
        if (principal.IsSuperuser) return true;
        if (!principal.MatchesPackage(packageId)) return false;

        // The access model first: a permission is necessary, ownership (below) remains necessary too. Administrators
        // hold every action (the evaluator says so), the superuser returned above.
        if (!await _access.IsAllowedAsync(principal, _repository.Current, packageId, Required(action), cancellationToken)) return false;

        // An administrator skips ownership, never the key's own limits: a push-only key an
        // administrator made for CI stays push-only.
        if (principal.IsAdministrator)
        {
            switch (action)
            {
                case PackageAction.Push:
                    var exists = await ExistsAsync(packageId, cancellationToken)
                                 || await _ownership.HasOwnersAsync(packageId, cancellationToken);

                    return exists
                        ? principal.HasScope(ApiKeyScopes.Push) || principal.HasScope(ApiKeyScopes.PushVersion)
                        : principal.HasScope(ApiKeyScopes.Push);

                case PackageAction.Manage:
                case PackageAction.Annotate:
                    return principal.HasScope(ApiKeyScopes.Unlist);

                default:
                    return false;
            }
        }

        var isOwner = await _ownership.IsOwnerAsync(principal.UserId, principal.UserName, packageId, cancellationToken);

        switch (action)
        {
            case PackageAction.Push:
                if (isOwner)
                {
                    return principal.HasScope(ApiKeyScopes.Push) || principal.HasScope(ApiKeyScopes.PushVersion);
                }

                // A new ID may be claimed; an existing one without owners may not.
                return principal.HasScope(ApiKeyScopes.Push)
                       && !await _ownership.HasOwnersAsync(packageId, cancellationToken)
                       && !await ExistsAsync(packageId, cancellationToken);

            case PackageAction.Manage:
            case PackageAction.Annotate:
                return isOwner && principal.HasScope(ApiKeyScopes.Unlist);

            default:
                return false;
        }
    }

    // Whether the ID names a package of the current repository, whatever its format: a NuGet package id, an npm package name, or a Maven group:artifact.
    private async Task<bool> ExistsAsync(string? packageId, CancellationToken cancellationToken)
    {
        if (_repository.Current.Format == RepositoryFormat.Npm && _context != null)
        {
            var name = packageId?.ToLowerInvariant();

            return await _context.NpmPackages!.AnyAsync(p => p.Name == name, cancellationToken);
        }

        if (_repository.Current.Format == RepositoryFormat.Maven && _context != null)
        {
            // A Maven artifact is "group:artifact"; both parts are case-sensitive.
            if (!MavenNames.TryParseArtifactName(packageId, out var groupId, out var artifactId)) return false;

            return await _context.MavenArtifacts!.AnyAsync(a => a.GroupId == groupId && a.ArtifactId == artifactId, cancellationToken);
        }

        return await _packages.ExistsAsync(packageId, cancellationToken);
    }

    private static AccessAction Required(PackageAction action) => action switch
    {
        PackageAction.Push => AccessAction.Deploy,
        PackageAction.Annotate => AccessAction.Annotate,
        _ => AccessAction.Delete,
    };

    public async Task OnPushedAsync(BaGetPrincipal? principal, string? packageId, bool isNewId, CancellationToken cancellationToken)
    {
        // Only a new ID is claimed: an administrator may push to an existing ownerless one,
        // and that must leave it ownerless.
        if (!isNewId || principal == null || principal.IsSuperuser || packageId == null) return;
        if (await _ownership.HasOwnersAsync(packageId, cancellationToken)) return;

        // Two users pushing the same new ID at once may both become owners. That is
        // safe, if surprising: each could only have done so with the push scope.
        var result = await _ownership.AddOwnerAsync(packageId, principal.UserName, cancellationToken);

        _logger.LogInformation(
            "{UserName} pushed new package {PackageId} and became its owner: {Result}",
            principal.UserName,
            packageId,
            result);
    }
}