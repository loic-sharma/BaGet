using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// The owners' screen for a Maven artifact: its owners, versions and the files of the chosen one, with deletion of a file, a version or the
/// whole artifact, and the clean-up of old snapshot builds. Every change goes through <see cref="IMavenRepository"/> as the signed-in user
/// (the session's principal, never a typed key), so the screen can do what the same user's API key could through the repository routes and no
/// more: the access model and ownership apply identically. Opening it takes ownership (directly or through an organization) or
/// administration; deleting also asks for the password again, since it cannot be undone. Works in the request's repository. Scoped:
/// <see cref="EnterAsync"/> runs once per request (see <see cref="PackageManageUseCase"/>).
/// </summary>
public sealed class MavenManageUseCase
{
    /// <summary>What every write handler answers on a read-only server.</summary>
    public const string ReadOnlyMessage = PackageScreenServices.ReadOnlyMessage;

    private readonly PackageScreenServices _services;
    private readonly IMavenRepository _maven;
    private readonly ILogger<MavenManageUseCase> _logger;

    private bool _entered;
    private string? _enteredKey;
    private bool _enteredWrite;
    private ScreenResult<MavenManageViewModel>? _refusal;
    private string _group = string.Empty;
    private string _artifact = string.Empty;
    private string? _version;
    private bool _isAdministrator;
    private bool _actingAsAdministrator;

    /// <summary>Creates the use case.</summary>
    public MavenManageUseCase(PackageScreenServices services, IMavenRepository maven, ILogger<MavenManageUseCase> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _maven = maven ?? throw new ArgumentNullException(nameof(maven));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string Name => MavenNames.ArtifactName(_group, _artifact);

    /// <summary>
    /// The check that runs before every handler: valid coordinates, a local Maven repository, ownership or administration, the maintenance
    /// gate and read-only mode for a write. Returns the answer that ends the request, or null to run the handler.
    /// </summary>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="group">The group id from the address.</param>
    /// <param name="artifact">The artifact id from the address.</param>
    /// <param name="version">The version whose files are listed (<c>version</c> of the form or query), or null.</param>
    /// <param name="handler">The handler about to run (for the administrator audit line).</param>
    /// <param name="isWrite">Whether the request is a POST.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ScreenResult<MavenManageViewModel>?> EnterAsync(ClaimsPrincipal caller, string? group, string? artifact, string? version, string? handler, bool isWrite, CancellationToken cancellationToken)
    {
        var key = group + "\n" + artifact + "\n" + version;
        if (_entered && _enteredKey == key && _enteredWrite == isWrite) return _refusal;

        _refusal = await CheckAsync(caller, group, artifact, version, handler, isWrite, cancellationToken).ConfigureAwait(false);
        _entered = true;
        _enteredKey = key;
        _enteredWrite = isWrite;

        return _refusal;
    }

    private async Task<ScreenResult<MavenManageViewModel>?> CheckAsync(ClaimsPrincipal caller, string? group, string? artifact, string? version, string? handler, bool isWrite, CancellationToken cancellationToken)
    {
        if (!MavenNames.IsValidGroupId(group) || !MavenNames.IsValidArtifactId(artifact) || (version != null && !MavenNames.IsValidVersion(version)))
        {
            return ScreenResult<MavenManageViewModel>.NotFound();
        }

        _group = group!;
        _artifact = artifact!;
        _version = version;

        // Owners and the owner tools exist where artifacts are deployed: only a local Maven repository (a page of another format is
        // 404, as the repository check says; the controller's own check can run before it).
        var current = _services.Repository.Current;
        if (current.Kind != RepositoryKind.Local || current.Format != RepositoryFormat.Maven)
        {
            return ScreenResult<MavenManageViewModel>.NotFound();
        }

        var isOwner = await _services.Ownership.IsOwnerAsync(_services.Users.GetUserId(caller), caller.Identity?.Name, Name, cancellationToken).ConfigureAwait(false);
        _isAdministrator = await _services.Administrators.IsAdministratorAsync(caller, cancellationToken).ConfigureAwait(false);
        if (!isOwner && !_isAdministrator)
        {
            return await _services.CanSeeAsync(caller, Name, cancellationToken).ConfigureAwait(false)
                ? ScreenResult<MavenManageViewModel>.Forbidden()
                : ScreenResult<MavenManageViewModel>.NotFound();
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

                return ScreenResult<MavenManageViewModel>.Ok(screen);
            }

            if (_actingAsAdministrator)
            {
                _logger.LogWarning("Administrator {Admin} ran {Handler} on Maven artifact {Artifact}", caller.Identity?.Name, handler, Name);
            }
        }

        return null;
    }

    /// <summary>The GET handler: loads the artifact, 404 when it does not exist.</summary>
    public async Task<ScreenResult<MavenManageViewModel>> GetAsync(ClaimsPrincipal caller, string? group, string? artifact, string? version, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, group, artifact, version, null, false, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var screen = await LoadAsync(caller, cancellationToken).ConfigureAwait(false);

        return screen.Info == null ? ScreenResult<MavenManageViewModel>.NotFound() : ScreenResult<MavenManageViewModel>.Ok(screen);
    }

    private async Task<MavenManageViewModel> LoadAsync(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var confirmation = await _services.ConfirmationAsync(caller, cancellationToken).ConfigureAwait(false);
        var info = await _maven.GetArtifactAsync(_group, _artifact, cancellationToken).ConfigureAwait(false);

        return new MavenManageViewModel
        {
            IsAdministrator = _isAdministrator,
            ActingAsAdministrator = _actingAsAdministrator,
            HasPassword = confirmation.HasPassword,
            Reauthenticated = confirmation.Reauthenticated,
            CanReauthenticate = confirmation.CanReauthenticate,
            Group = _group,
            Artifact = _artifact,
            Version = _version,
            Info = info,
            Selected = info != null && _version != null && info.Versions.Any(v => v.Version == _version)
                ? await _maven.GetVersionAsync(_group, _artifact, _version, cancellationToken).ConfigureAwait(false)
                : null,
            Owners = await _services.Ownership.GetOwnersAsync(Name, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<ScreenResult<MavenManageViewModel>> DoneAsync(ClaimsPrincipal caller, string message, bool failed, CancellationToken cancellationToken)
    {
        var screen = await LoadAsync(caller, cancellationToken).ConfigureAwait(false);
        screen.Message = message;
        screen.Failed = failed;

        return ScreenResult<MavenManageViewModel>.Ok(screen);
    }

    private Task<ScreenResult<MavenManageViewModel>> DoneAsync(ClaimsPrincipal caller, MavenResult result, string success, CancellationToken cancellationToken)
        => DoneAsync(caller, result.Succeeded ? success : result.Message ?? "Not done.", !result.Succeeded, cancellationToken);

    /// <summary>Adds an owner.</summary>
    public async Task<ScreenResult<MavenManageViewModel>> AddOwnerAsync(ClaimsPrincipal caller, string? group, string? artifact, string? version, string? ownerName, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, group, artifact, version, "AddOwner", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var result = string.IsNullOrWhiteSpace(ownerName)
            ? OwnerChangeResult.UnknownAccount
            : await _services.Ownership.AddOwnerAsync(Name, ownerName.Trim(), cancellationToken).ConfigureAwait(false);

        return await DoneAsync(caller, result == OwnerChangeResult.Success ? $"{ownerName} is now an owner." : PackageScreenServices.Describe(result, "artifact"), result != OwnerChangeResult.Success, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes an owner.</summary>
    public async Task<ScreenResult<MavenManageViewModel>> RemoveOwnerAsync(ClaimsPrincipal caller, string? group, string? artifact, string? version, string? ownerName, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, group, artifact, version, "RemoveOwner", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        #pragma warning disable CS8604
        var result = await _services.Ownership.RemoveOwnerAsync(Name, ownerName, cancellationToken).ConfigureAwait(false);
        #pragma warning restore CS8604

        return await DoneAsync(caller, result == OwnerChangeResult.Success ? $"{ownerName} is no longer an owner." : PackageScreenServices.Describe(result, "artifact"), result != OwnerChangeResult.Success, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes one file of the chosen version (and its checksums with it), after the password is confirmed.</summary>
    public async Task<ScreenResult<MavenManageViewModel>> DeleteFileAsync(ClaimsPrincipal caller, string? group, string? artifact, string? version, string? fileName, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, group, artifact, version, "DeleteFile", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        if (_version == null) return await DoneAsync(caller, "Choose a version first.", failed: true, cancellationToken).ConfigureAwait(false);

        var result = await _maven.DeleteFileAsync(await _services.CallerAsync(caller).ConfigureAwait(false), new MavenCoordinate(_group, _artifact, _version), fileName ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) _logger.LogWarning("{User} deleted {File} of {Artifact} {Version}", caller.Identity?.Name, fileName, Name, _version);

        return await AfterDeleteAsync(caller, result, $"{fileName} is deleted.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes one version with its files, after the password is confirmed.</summary>
    public async Task<ScreenResult<MavenManageViewModel>> DeleteVersionAsync(ClaimsPrincipal caller, string? group, string? artifact, string? selectedVersion, string? version, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, group, artifact, selectedVersion, "DeleteVersion", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        if (!MavenNames.IsValidVersion(version)) return await DoneAsync(caller, "Choose a version.", failed: true, cancellationToken).ConfigureAwait(false);

        var result = await _maven.DeleteVersionAsync(await _services.CallerAsync(caller).ConfigureAwait(false), new MavenCoordinate(_group, _artifact, version), cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) _logger.LogWarning("{User} deleted {Artifact} {Version}", caller.Identity?.Name, Name, version);
        if (_version == version) _version = null;

        return await AfterDeleteAsync(caller, result, $"{Name} {version} is deleted.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes every version of the artifact, after the password is confirmed.</summary>
    public async Task<ScreenResult<MavenManageViewModel>> DeleteArtifactAsync(ClaimsPrincipal caller, string? group, string? artifact, string? version, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, group, artifact, version, "DeleteArtifact", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        var result = await _maven.DeleteArtifactAsync(await _services.CallerAsync(caller).ConfigureAwait(false), _group, _artifact, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return await DoneAsync(caller, result, string.Empty, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("{User} deleted Maven artifact {Artifact}", caller.Identity?.Name, Name);

        return ScreenResult<MavenManageViewModel>.GoTo(new ScreenNext(ScreenCatalog.MavenArtifacts));
    }

    /// <summary>Deletes the builds of a snapshot version older than the newest <paramref name="keep"/>.</summary>
    public async Task<ScreenResult<MavenManageViewModel>> CleanupAsync(ClaimsPrincipal caller, string? group, string? artifact, string? selectedVersion, string? version, string? keep, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, group, artifact, selectedVersion, "Cleanup", true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        if (!MavenNames.IsValidVersion(version)) return await DoneAsync(caller, "Choose a snapshot version.", failed: true, cancellationToken).ConfigureAwait(false);
        if (!int.TryParse(keep, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1)
        {
            return await DoneAsync(caller, "Enter how many builds to keep (at least 1).", failed: true, cancellationToken).ConfigureAwait(false);
        }

        var result = await _maven.CleanupSnapshotsAsync(await _services.CallerAsync(caller).ConfigureAwait(false), new MavenCoordinate(_group, _artifact, version), count, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) _logger.LogWarning("{User} cleaned up the snapshot builds of {Artifact} {Version}, keeping {Keep}", caller.Identity?.Name, Name, version, count);

        return await DoneAsync(caller, result, result.Message ?? "Done.", cancellationToken).ConfigureAwait(false);
    }

    // The last file takes the version, the last version the artifact: nothing left to manage.
    private async Task<ScreenResult<MavenManageViewModel>> AfterDeleteAsync(ClaimsPrincipal caller, MavenResult result, string success, CancellationToken cancellationToken)
    {
        if (result.Succeeded && await _maven.GetArtifactAsync(_group, _artifact, cancellationToken).ConfigureAwait(false) == null)
        {
            return ScreenResult<MavenManageViewModel>.GoTo(new ScreenNext(ScreenCatalog.MavenArtifacts));
        }

        return await DoneAsync(caller, result, success, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Null when the signed-in user confirmed with their password, otherwise the screen to show.</summary>
    private async Task<ScreenResult<MavenManageViewModel>?> RefuseUnconfirmedAsync(ClaimsPrincipal caller, string? password, CancellationToken cancellationToken)
    {
        var current = await _services.Users.GetUserAsync(caller).ConfigureAwait(false);
        if (current == null) return ScreenResult<MavenManageViewModel>.SignIn();

        return await _services.IsConfirmedAsync(current, password, cancellationToken).ConfigureAwait(false)
            ? null
            : await DoneAsync(caller, "The confirmation is incorrect, so nothing was deleted.", failed: true, cancellationToken).ConfigureAwait(false);
    }
}
