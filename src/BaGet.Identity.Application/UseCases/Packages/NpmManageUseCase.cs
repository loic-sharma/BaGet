using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// The owners' screen for an npm package: its owners, dist-tags, per-version deprecation and unpublishing. Every change goes through
/// <see cref="INpmRegistry"/> as the signed-in user (the session's principal, never a typed key), so the screen can do what the same user's API
/// key could through the registry routes and no more: the access model, ownership, and the unpublish window apply identically. Opening it
/// takes ownership (directly or through an organization) or administration; unpublishing also asks for the password again, since it cannot be
/// undone. Works in the request's repository. Scoped: <see cref="EnterAsync"/> runs once per request (see <see cref="PackageManageUseCase"/>).
/// </summary>
public sealed class NpmManageUseCase
{
    /// <summary>What every write handler answers on a read-only server.</summary>
    public const string ReadOnlyMessage = PackageScreenServices.ReadOnlyMessage;

    private readonly PackageScreenServices _services;
    private readonly INpmRegistry _registry;
    private readonly IUrlGenerator _urls;
    private readonly IOptionsSnapshot<NpmOptions> _npm;
    private readonly ILogger<NpmManageUseCase> _logger;

    private bool _entered;
    private string? _enteredId;
    private bool _enteredWrite;
    private ScreenResult<NpmManageViewModel>? _refusal;
    private string _name = string.Empty;
    private bool _isAdministrator;
    private bool _actingAsAdministrator;

    /// <summary>Creates the use case.</summary>
    public NpmManageUseCase(
        PackageScreenServices services,
        INpmRegistry registry,
        IUrlGenerator urls,
        IOptionsSnapshot<NpmOptions> npm,
        ILogger<NpmManageUseCase> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _npm = npm ?? throw new ArgumentNullException(nameof(npm));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The check that runs before every handler: a valid name, a local repository, ownership or administration, the maintenance gate and
    /// read-only mode for a write. Returns the answer that ends the request, or null to run the handler.
    /// </summary>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="id">The package name from the address.</param>
    /// <param name="handler">The handler about to run (for the administrator audit line).</param>
    /// <param name="isWrite">Whether the request is a POST.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ScreenResult<NpmManageViewModel>?> EnterAsync(ClaimsPrincipal caller, string? id, string? handler, bool isWrite, CancellationToken cancellationToken)
    {
        if (_entered && _enteredId == id && _enteredWrite == isWrite) return _refusal;

        _refusal = await CheckAsync(caller, id, handler, isWrite, cancellationToken).ConfigureAwait(false);
        _entered = true;
        _enteredId = id;
        _enteredWrite = isWrite;

        return _refusal;
    }

    private async Task<ScreenResult<NpmManageViewModel>?> CheckAsync(ClaimsPrincipal caller, string? id, string? handler, bool isWrite, CancellationToken cancellationToken)
    {
        if (!NpmNames.TryParse(id, out var name, out _))
        {
            return ScreenResult<NpmManageViewModel>.NotFound();
        }

        _name = name;

        // Owners and the owner tools exist where packages are published: a remote repository only caches an upstream's (every
        // change is refused there) and a virtual one holds none, so neither has this screen (the repository check says the same;
        // the controller's own check can run before it, so the rule is kept here too).
        if (_services.Repository.Current.Kind != RepositoryKind.Local)
        {
            return ScreenResult<NpmManageViewModel>.NotFound();
        }

        var isOwner = await _services.Ownership.IsOwnerAsync(_services.Users.GetUserId(caller), caller.Identity?.Name, _name, cancellationToken).ConfigureAwait(false);
        _isAdministrator = await _services.Administrators.IsAdministratorAsync(caller, cancellationToken).ConfigureAwait(false);
        if (!isOwner && !_isAdministrator)
        {
            return await _services.CanSeeAsync(caller, _name, cancellationToken).ConfigureAwait(false)
                ? ScreenResult<NpmManageViewModel>.Forbidden()
                : ScreenResult<NpmManageViewModel>.NotFound();
        }

        _actingAsAdministrator = _isAdministrator && !isOwner;

        if (isWrite)
        {
            // A write: refused while a database copy has the gate closed, and in read-only mode, for every handler at once.
            _services.Services.GetService<MaintenanceGate>()?.ThrowIfClosed();

            if (_services.IsReadOnlyMode)
            {
                var screen = await LoadAsync(caller, cancellationToken).ConfigureAwait(false);
                screen.Message = ReadOnlyMessage;
                screen.Failed = true;

                return ScreenResult<NpmManageViewModel>.Ok(screen);
            }

            if (_actingAsAdministrator)
            {
                _logger.LogWarning("Administrator {Admin} ran {Handler} on npm package {Package}", caller.Identity?.Name, handler, _name);
            }
        }

        return null;
    }

    /// <summary>The GET handler: loads the package, 404 when it does not exist.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> GetAsync(ClaimsPrincipal caller, string? id, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, null, false, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var screen = await LoadAsync(caller, cancellationToken).ConfigureAwait(false);

        return screen.Package == null ? ScreenResult<NpmManageViewModel>.NotFound() : ScreenResult<NpmManageViewModel>.Ok(screen);
    }

    private async Task<NpmManageViewModel> LoadAsync(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var confirmation = await _services.ConfirmationAsync(caller, cancellationToken).ConfigureAwait(false);

        return new NpmManageViewModel
        {
            IsAdministrator = _isAdministrator,
            ActingAsAdministrator = _actingAsAdministrator,
            HasPassword = confirmation.HasPassword,
            Reauthenticated = confirmation.Reauthenticated,
            CanReauthenticate = confirmation.CanReauthenticate,
            RegistryUrl = _urls.GetNpmRegistryUrl(),
            UnpublishWindow = _npm.Value.UnpublishWindow,
            Id = _name,
            Package = await _registry.GetPackageAsync(_name, cancellationToken).ConfigureAwait(false),
            Owners = await _services.Ownership.GetOwnersAsync(_name, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<ScreenResult<NpmManageViewModel>> DoneAsync(ClaimsPrincipal caller, string message, bool failed, CancellationToken cancellationToken)
    {
        var screen = await LoadAsync(caller, cancellationToken).ConfigureAwait(false);
        screen.Message = message;
        screen.Failed = failed;

        return ScreenResult<NpmManageViewModel>.Ok(screen);
    }

    private Task<ScreenResult<NpmManageViewModel>> DoneAsync(ClaimsPrincipal caller, NpmResult result, string success, CancellationToken cancellationToken)
        => DoneAsync(caller, result.Succeeded ? success : result.Message ?? "Not done.", !result.Succeeded, cancellationToken);

    /// <summary>Adds an owner.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> AddOwnerAsync(ClaimsPrincipal caller, string? id, string? ownerName, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "AddOwner", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var result = string.IsNullOrWhiteSpace(ownerName)
            ? OwnerChangeResult.UnknownAccount
            : await _services.Ownership.AddOwnerAsync(_name, ownerName.Trim(), cancellationToken).ConfigureAwait(false);

        return await DoneAsync(caller, result == OwnerChangeResult.Success ? $"{ownerName} is now an owner." : PackageScreenServices.Describe(result, "package"), result != OwnerChangeResult.Success, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes an owner.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> RemoveOwnerAsync(ClaimsPrincipal caller, string? id, string? ownerName, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "RemoveOwner", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        #pragma warning disable CS8604
        var result = await _services.Ownership.RemoveOwnerAsync(_name, ownerName, cancellationToken).ConfigureAwait(false);
        #pragma warning restore CS8604

        return await DoneAsync(caller, result == OwnerChangeResult.Success ? $"{ownerName} is no longer an owner." : PackageScreenServices.Describe(result, "package"), result != OwnerChangeResult.Success, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Points a dist-tag at a version.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> SetTagAsync(ClaimsPrincipal caller, string? id, string? tag, string? version, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "SetTag", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var result = await _registry.SetDistTagAsync(await _services.CallerAsync(caller).ConfigureAwait(false), _name, tag?.Trim() ?? string.Empty, version ?? string.Empty, cancellationToken).ConfigureAwait(false);

        return await DoneAsync(caller, result, $"{tag} now points at {version}.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a dist-tag.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> RemoveTagAsync(ClaimsPrincipal caller, string? id, string? tag, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "RemoveTag", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var result = await _registry.RemoveDistTagAsync(await _services.CallerAsync(caller).ConfigureAwait(false), _name, tag ?? string.Empty, cancellationToken).ConfigureAwait(false);

        return await DoneAsync(caller, result, $"The tag {tag} is removed.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deprecates a version.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> DeprecateAsync(ClaimsPrincipal caller, string? id, string? version, string? message, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "Deprecate", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        if (string.IsNullOrWhiteSpace(message))
        {
            return await DoneAsync(caller, "Write the deprecation message.", failed: true, cancellationToken).ConfigureAwait(false);
        }

        var result = await _registry.DeprecateAsync(await _services.CallerAsync(caller).ConfigureAwait(false), _name, new Dictionary<string, string?> { [version ?? string.Empty] = message.Trim() }, cancellationToken).ConfigureAwait(false);

        return await DoneAsync(caller, result, $"{_name}@{version} is deprecated.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a version's deprecation.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> UndeprecateAsync(ClaimsPrincipal caller, string? id, string? version, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "Undeprecate", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var result = await _registry.DeprecateAsync(await _services.CallerAsync(caller).ConfigureAwait(false), _name, new Dictionary<string, string?> { [version ?? string.Empty] = null }, cancellationToken).ConfigureAwait(false);

        return await DoneAsync(caller, result, $"{_name}@{version} is no longer deprecated.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unpublishes one version and deletes its tarball, after the password is confirmed.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> UnpublishAsync(ClaimsPrincipal caller, string? id, string? version, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "Unpublish", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        var result = await _registry.UnpublishVersionsAsync(await _services.CallerAsync(caller).ConfigureAwait(false), _name, new[] { version ?? string.Empty }, newTags: null, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) _logger.LogWarning("{User} unpublished {Package}@{Version}", caller.Identity?.Name, _name, version);

        // The last version takes the package with it: nothing left to manage.
        if (result.Succeeded && await _registry.GetPackageAsync(_name, cancellationToken).ConfigureAwait(false) == null)
        {
            return ScreenResult<NpmManageViewModel>.GoTo(new ScreenNext(ScreenCatalog.NpmPackages));
        }

        return await DoneAsync(caller, result, $"{_name}@{version} is unpublished. Its tarball is deleted.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unpublishes every version of the package, after the password is confirmed.</summary>
    public async Task<ScreenResult<NpmManageViewModel>> UnpublishAllAsync(ClaimsPrincipal caller, string? id, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, id, "UnpublishAll", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        var result = await _registry.UnpublishPackageAsync(await _services.CallerAsync(caller).ConfigureAwait(false), _name, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return await DoneAsync(caller, result, string.Empty, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("{User} unpublished npm package {Package}", caller.Identity?.Name, _name);

        return ScreenResult<NpmManageViewModel>.GoTo(new ScreenNext(ScreenCatalog.NpmPackages));
    }

    /// <summary>Null when the signed-in user confirmed with their password, otherwise the screen to show.</summary>
    private async Task<ScreenResult<NpmManageViewModel>?> RefuseUnconfirmedAsync(ClaimsPrincipal caller, string? password, CancellationToken cancellationToken)
    {
        var current = await _services.Users.GetUserAsync(caller).ConfigureAwait(false);
        if (current == null) return ScreenResult<NpmManageViewModel>.SignIn();

        return await _services.IsConfirmedAsync(current, password, cancellationToken).ConfigureAwait(false)
            ? null
            : await DoneAsync(caller, "The confirmation is incorrect, so nothing was unpublished.", failed: true, cancellationToken).ConfigureAwait(false);
    }
}
