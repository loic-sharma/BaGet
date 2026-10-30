using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BaGet.Identity.Application.Models.Packages;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// The delete tools of a Docker image: delete a tag, a manifest (with its tags), remove the layers no manifest uses, or delete the whole
/// image. Every change goes through <see cref="IOciRegistry"/> after the same check the registry applies to a <c>DELETE</c>
/// (<see cref="IOciAccess"/>: the Delete action on the image, for the signed-in user's session principal, never a typed key), so the screen
/// can do what the same user's API key could through the registry routes and no more. Opening it needs Delete; every change also asks for the
/// password again, since it cannot be undone. Works in the request's repository. Scoped: <see cref="EnterAsync"/> runs once per request (see
/// <see cref="PackageManageUseCase"/>).
/// </summary>
public sealed class DockerManageUseCase
{
    /// <summary>What every write handler answers on a read-only server.</summary>
    public const string ReadOnlyMessage = PackageScreenServices.ReadOnlyMessage;

    private readonly PackageScreenServices _services;
    private readonly IOciRegistry _registry;
    private readonly IOciAccess _access;
    private readonly ILogger<DockerManageUseCase> _logger;

    private bool _entered;
    private string? _enteredImage;
    private bool _enteredWrite;
    private ScreenResult<DockerManageViewModel>? _refusal;

    /// <summary>Creates the use case.</summary>
    public DockerManageUseCase(PackageScreenServices services, IOciRegistry registry, IOciAccess access, ILogger<DockerManageUseCase> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The check that runs before every handler: a valid image name, a local Docker repository, the Delete action on the image, the
    /// maintenance gate and read-only mode for a write. Returns the answer that ends the request, or null to run the handler.
    /// </summary>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="image">The image name from the address (it may contain slashes).</param>
    /// <param name="isWrite">Whether the request is a POST.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ScreenResult<DockerManageViewModel>?> EnterAsync(ClaimsPrincipal caller, string? image, bool isWrite, CancellationToken cancellationToken)
    {
        if (_entered && _enteredImage == image && _enteredWrite == isWrite) return _refusal;

        _refusal = await CheckAsync(caller, image, isWrite, cancellationToken).ConfigureAwait(false);
        _entered = true;
        _enteredImage = image;
        _enteredWrite = isWrite;

        return _refusal;
    }

    private async Task<ScreenResult<DockerManageViewModel>?> CheckAsync(ClaimsPrincipal caller, string? image, bool isWrite, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image))
        {
            return ScreenResult<DockerManageViewModel>.NotFound();
        }

        // The tools exist where images are pushed: only a local Docker repository (a page of another format is 404, as the
        // repository check says; the controller's own check can run before it).
        var current = _services.Repository.Current;
        if (current.Kind != RepositoryKind.Local || current.Format != RepositoryFormat.Docker)
        {
            return ScreenResult<DockerManageViewModel>.NotFound();
        }

        // The rule of a DELETE on the registry: the Delete action on the image, for this user.
        var principal = await _services.CallerAsync(caller).ConfigureAwait(false);
        if (principal == null || !await _access.IsAllowedAsync(principal, current, image!, OciActions.Delete, cancellationToken).ConfigureAwait(false))
        {
            return await _services.CanSeeAsync(caller, image, cancellationToken).ConfigureAwait(false)
                ? ScreenResult<DockerManageViewModel>.Forbidden()
                : ScreenResult<DockerManageViewModel>.NotFound();
        }

        if (isWrite)
        {
            // A write: refused while a database copy has the gate closed, and in read-only mode, for every handler at once.
            _services.Services.GetService<MaintenanceGate>()?.ThrowIfClosed();

            if (_services.IsReadOnlyMode)
            {
                var screen = await LoadAsync(caller, image, cancellationToken).ConfigureAwait(false);
                screen.Message = ReadOnlyMessage;
                screen.Failed = true;

                return ScreenResult<DockerManageViewModel>.Ok(screen);
            }
        }

        return null;
    }

    /// <summary>The GET handler: loads the image, 404 when it does not exist.</summary>
    public async Task<ScreenResult<DockerManageViewModel>> GetAsync(ClaimsPrincipal caller, string? image, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, image, false, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var screen = await LoadAsync(caller, image, cancellationToken).ConfigureAwait(false);

        return screen.Info == null ? ScreenResult<DockerManageViewModel>.NotFound() : ScreenResult<DockerManageViewModel>.Ok(screen);
    }

    private async Task<DockerManageViewModel> LoadAsync(ClaimsPrincipal caller, string? image, CancellationToken cancellationToken)
    {
        var confirmation = await _services.ConfirmationAsync(caller, cancellationToken).ConfigureAwait(false);

        return new DockerManageViewModel
        {
            HasPassword = confirmation.HasPassword,
            Reauthenticated = confirmation.Reauthenticated,
            CanReauthenticate = confirmation.CanReauthenticate,
            Image = image,
            Info = await _registry.GetImageAsync(image!, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<ScreenResult<DockerManageViewModel>> DoneAsync(ClaimsPrincipal caller, string? image, string message, bool failed, CancellationToken cancellationToken)
    {
        var screen = await LoadAsync(caller, image, cancellationToken).ConfigureAwait(false);
        screen.Message = message;
        screen.Failed = failed;

        return ScreenResult<DockerManageViewModel>.Ok(screen);
    }

    /// <summary>Deletes a tag (the manifest it points at stays), after the password is confirmed.</summary>
    public async Task<ScreenResult<DockerManageViewModel>> DeleteTagAsync(ClaimsPrincipal caller, string? image, string? tag, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, image, true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, image, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        if (!OciNames.IsValidTag(tag)) return await DoneAsync(caller, image, "Choose a tag.", failed: true, cancellationToken).ConfigureAwait(false);

        var deleted = await _registry.DeleteTagAsync(image!, tag!, cancellationToken).ConfigureAwait(false);
        if (deleted) _logger.LogWarning("{User} deleted tag {Tag} of Docker image {Image}", caller.Identity?.Name, tag, image);

        return await DoneAsync(caller, image, deleted ? $"Tag {tag} is deleted." : "There is no such tag.", !deleted, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a manifest with the tags that point at it, after the password is confirmed.</summary>
    public async Task<ScreenResult<DockerManageViewModel>> DeleteManifestAsync(ClaimsPrincipal caller, string? image, string? digest, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, image, true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, image, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        if (!OciNames.IsValidDigest(digest)) return await DoneAsync(caller, image, "Choose a manifest.", failed: true, cancellationToken).ConfigureAwait(false);

        var deleted = await _registry.DeleteManifestAsync(image!, digest!, cancellationToken).ConfigureAwait(false);
        if (deleted) _logger.LogWarning("{User} deleted manifest {Digest} of Docker image {Image}", caller.Identity?.Name, digest, image);

        return await DoneAsync(caller, image, deleted ? "The manifest and its tags are deleted." : "There is no such manifest.", !deleted, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the layers no manifest uses (older than the grace period), after the password is confirmed.</summary>
    public async Task<ScreenResult<DockerManageViewModel>> PruneAsync(ClaimsPrincipal caller, string? image, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, image, true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, image, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        var result = await _registry.PruneAsync(image!, cancellationToken).ConfigureAwait(false);
        if (result.Blobs > 0) _logger.LogWarning("{User} pruned {Count} unused layers of Docker image {Image}", caller.Identity?.Name, result.Blobs, image);

        return await DoneAsync(
            caller,
            image,
            result.Blobs == 0
                ? "There was no unused layer old enough to remove."
                : $"Removed {result.Blobs} unused layer{(result.Blobs == 1 ? string.Empty : "s")} ({result.Bytes:N0} bytes).",
            failed: false,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the image with every manifest, tag and layer only it holds, after the password is confirmed.</summary>
    public async Task<ScreenResult<DockerManageViewModel>> DeleteImageAsync(ClaimsPrincipal caller, string? image, string? password, CancellationToken cancellationToken)
    {
        var refusal = await EnterAsync(caller, image, true, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var unconfirmed = await RefuseUnconfirmedAsync(caller, image, password, cancellationToken).ConfigureAwait(false);
        if (unconfirmed != null) return unconfirmed;

        if (!await _registry.DeleteImageAsync(image!, cancellationToken).ConfigureAwait(false))
        {
            return await DoneAsync(caller, image, "There is no such image.", failed: true, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogWarning("{User} deleted Docker image {Image}", caller.Identity?.Name, image);

        return ScreenResult<DockerManageViewModel>.GoTo(new ScreenNext(ScreenCatalog.DockerImages));
    }

    /// <summary>Null when the signed-in user confirmed with their password, otherwise the screen to show.</summary>
    private async Task<ScreenResult<DockerManageViewModel>?> RefuseUnconfirmedAsync(ClaimsPrincipal caller, string? image, string? password, CancellationToken cancellationToken)
    {
        var current = await _services.Users.GetUserAsync(caller).ConfigureAwait(false);
        if (current == null) return ScreenResult<DockerManageViewModel>.SignIn();

        return await _services.IsConfirmedAsync(current, password, cancellationToken).ConfigureAwait(false)
            ? null
            : await DoneAsync(caller, image, "The confirmation is incorrect, so nothing was deleted.", failed: true, cancellationToken).ConfigureAwait(false);
    }
}
