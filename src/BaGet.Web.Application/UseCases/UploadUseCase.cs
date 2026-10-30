using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Versioning;
using BaGet.Web.Application.Models.Packages;
using BaGet.Web.Application.Models.Upload;
using BaGet.Web.Application.Services;

namespace BaGet.Web.Application.UseCases;

using NuGetPackageType = NuGet.Packaging.Core.PackageType;

/// <summary>
/// Uploading a NuGet package through the browser, in two steps: the first stages the file and shows what it contains, the second
/// publishes it (or the third discards it). Every step goes through the same authorization and the same submission pipeline as
/// <c>dotnet nuget push</c>, so the browser can do what the pusher's own API key could, and no more. The use case of the Upload screen
/// (<c>UploadApiController</c>, which the view controller calls in process).
/// </summary>
/// <remarks>
/// Deliberately not an account page: those answer 404 while accounts are disabled, and this one must still work there - with a
/// pasted API key, exactly as the push endpoint requires one. Each step returns the <see cref="UploadViewModel"/> to render and
/// the status to answer it with; a refusal is a page with a message, never a bare error.
/// </remarks>
public sealed class UploadUseCase
{
    internal const string ReadOnlyMessage =
        "This server is in read-only mode, so nothing can be uploaded.";

    internal const string SignInMessage =
        "Sign in to upload a package.";

    internal const string ApiKeyRejectedMessage =
        "That API key was not accepted.";

    internal const string NoApiKeyMessage =
        "This server has no API key configured and no accounts, so there is nothing to "
        + "tell one visitor from another and nothing to authenticate an upload with. "
        + "Set \"ApiKey\", or enable \"Accounts:Enabled\", to upload through the browser.";

    internal const string UnknownTokenMessage =
        "This upload is no longer staged. It was cancelled, already published, or staged "
        + "by someone else - upload the file again.";

    internal const string UnreadableMessage =
        "This file could not be read as a package. Upload the .nupkg or .snupkg a build "
        + "produced, not a renamed or repacked copy.";

    internal const string TermsMessage =
        "Confirm that you have the right to publish this package before submitting.";

    internal const string InvalidSymbolPackageMessage =
        "This symbol package is not valid (its entries, a PDB that is not portable, or "
        + "PDBs that do not match the package).";

    internal const string NotASymbolPackageMessage =
        "A .snupkg must declare the SymbolsPackage package type in its own nuspec. This "
        + "file does not, so it is not a symbol package - and a symbols archive is not a "
        + "package either. Repack it as a symbol package, or upload the .nupkg instead.";

    internal const string CouldNotStageMessage =
        "The server could not store this upload. Try again.";

    private readonly IOptionsSnapshot<BaGetOptions> _options;
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly IPackageAuthorizationService _authorization;
    private readonly IPackageSubmissionService _submissions;
    private readonly ISymbolIndexingService _symbols;
    private readonly IPackageStagingService _staging;
    private readonly IPackageDatabase _packages;
    private readonly ILogger _logger;
    private readonly IScanGate _scanGate;
    private readonly UploadAuthenticator _authenticator;

    /// <summary>Creates the use case.</summary>
    public UploadUseCase(
        IOptionsSnapshot<BaGetOptions> options,
        IOptions<AccountsOptions> accounts,
        IPackageAuthorizationService authorization,
        IPackageSubmissionService submissions,
        ISymbolIndexingService symbols,
        IPackageStagingService staging,
        IPackageDatabase packages,
        ILogger<UploadUseCase> logger,
        IScanGate scanGate,
        UploadAuthenticator authenticator)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _scanGate = scanGate ?? throw new ArgumentNullException(nameof(scanGate));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private bool AccountsEnabled => _accounts.Value.Enabled;

    private bool IsReadOnlyMode => _options.Value.IsReadOnlyMode;

    private bool NothingCanAuthenticate => UploadAvailability.NothingCanAuthenticate(_options.Value, _accounts.Value);

    private string NotAuthenticatedMessage => AccountsEnabled ? SignInMessage : ApiKeyRejectedMessage;

    /// <summary>The page as a visitor first sees it (step one).</summary>
    public ScreenResult<UploadViewModel> Show(ClaimsPrincipal user, string? id) => ScreenResult<UploadViewModel>.Ok(NewModel(user, id));

    /// <summary>Step one: stages the file and describes what it contains.</summary>
    public async Task<ScreenResult<UploadViewModel>> StageAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        string? id,
        UploadedFile? package,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        return Result(await StageCoreAsync(user, key, id, package, apiKey, cancellationToken));
    }

    private async Task<UploadViewModel> StageCoreAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        string? id,
        UploadedFile? package,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        var model = NewModel(user, id);

        if (IsReadOnlyMode) return Refuse(model, 401, ReadOnlyMessage);
        if (NothingCanAuthenticate) return Refuse(model, 401, NoApiKeyMessage);

        var principal = await _authenticator.AuthenticateAsync(user, key, apiKey, cancellationToken);
        if (principal == null) return Refuse(model, 401, NotAuthenticatedMessage);

        if (package == null || package.Length == 0)
        {
            return Refuse(model, 400, "Choose a .nupkg or .snupkg file to upload.");
        }

        var fileName = Path.GetFileName(package.FileName) ?? string.Empty;
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".snupkg", StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(model, 400, "A package is a .nupkg file, and symbols are a .snupkg file.");
        }

        // The spooled upload is seekable, which is what reading the archive needs; one
        // that is not is copied once rather than reported as unreadable.
        using var upload = package.OpenReadStream();
        using var copy = upload.CanSeek ? null : await upload.AsTemporaryFileStreamAsync(cancellationToken);
        var content = copy ?? upload;

        var staged = await ReadOrNullAsync(content, cancellationToken);
        if (staged == null) return Refuse(model, 400, UnreadableMessage);

        // The content decides the pipeline, never the file's name. A .snupkg whose
        // nuspec does not declare the symbols package type is therefore not a symbol
        // package - and publishing it as a package would store a symbols archive as the
        // .nupkg of that ID and version, while the symbol endpoint reads the identical
        // bytes as symbols. Refused rather than published as something it is not.
        if (extension.Equals(".snupkg", StringComparison.OrdinalIgnoreCase) && !staged.IsSymbolPackage)
        {
            return Refuse(model, 400, NotASymbolPackageMessage);
        }

        // Authorized before the upload is stored: there is no point keeping a file its
        // uploader may not publish.
        if (!await _authorization.AuthorizeAsync(principal, PackageAction.Push, staged.Id, cancellationToken))
        {
            return Refuse(model, 403, Denied(staged.Id));
        }

        // The scanning policy of the repository may refuse a package with known vulnerabilities: said now, not after the upload.
        if (!staged.IsSymbolPackage)
        {
            var blocked = await RefuseIfBlockedAsync(model, content, cancellationToken);
            if (blocked != null) return blocked;
        }

        await DescribeSymbolTargetAsync(staged, cancellationToken);

        content.Position = 0;

        try
        {
            staged.Token = await _staging.StageAsync(content, principal.UserId, cancellationToken);
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            // A failure the server owns, reported the way the equivalent failure on the
            // confirm step is: a sentence with a status code, never a bare 500 or a
            // stack trace.
            _logger.LogError(e, "Could not stage an upload of {PackageId} {PackageVersion}", staged.Id, staged.Version);

            return Refuse(model, 500, CouldNotStageMessage);
        }

        model.Staged = staged;

        return model;
    }

    /// <summary>Step two: publishes the staged upload.</summary>
    public async Task<ScreenResult<UploadViewModel>> SubmitAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        string? id,
        string token,
        string? apiKey,
        bool acceptTerms,
        CancellationToken cancellationToken)
    {
        return Result(await SubmitCoreAsync(user, key, id, token, apiKey, acceptTerms, cancellationToken));
    }

    private async Task<UploadViewModel> SubmitCoreAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        string? id,
        string token,
        string? apiKey,
        bool acceptTerms,
        CancellationToken cancellationToken)
    {
        var model = NewModel(user, id);

        if (IsReadOnlyMode) return Refuse(model, 401, ReadOnlyMessage);
        if (NothingCanAuthenticate) return Refuse(model, 401, NoApiKeyMessage);

        var principal = await _authenticator.AuthenticateAsync(user, key, apiKey, cancellationToken);
        if (principal == null) return RefuseKey(model, token);

        // Whoever staged the upload is the only one who can open it, so another user's
        // token is indistinguishable from one that never existed.
        Stream content;
        using (var stagedStream = await _staging.OpenAsync(token, principal.UserId, cancellationToken))
        {
            if (stagedStream == null) return Refuse(model, 404, UnknownTokenMessage);

            // Copied and closed before anything is removed: an open handle on the staged
            // blob stops it being deleted on file systems that refuse to delete open files.
            content = await stagedStream.AsTemporaryFileStreamAsync(cancellationToken);
        }

        using var ownedContent = content;

        var staged = await ReadOrNullAsync(content, cancellationToken);
        if (staged == null)
        {
            await _staging.RemoveAsync(token, principal.UserId, cancellationToken);
            return Refuse(model, 400, UnreadableMessage);
        }

        staged.Token = token;
        await DescribeSymbolTargetAsync(staged, cancellationToken);

        // The checkbox is enforced here too, not only in the browser: the upload stays
        // staged so the box can be ticked and the preview submitted again.
        if (!acceptTerms)
        {
            model.Staged = staged;
            return Refuse(model, 400, TermsMessage);
        }

        // Re-checked at submission: ownership may have changed while the preview was on
        // screen, and the first check guarded the store, not the publish.
        if (!await _authorization.AuthorizeAsync(principal, PackageAction.Push, staged.Id, cancellationToken))
        {
            await _staging.RemoveAsync(token, principal.UserId, cancellationToken);
            return Refuse(model, 403, Denied(staged.Id));
        }

        content.Position = 0;

        return staged.IsSymbolPackage
            ? await PublishSymbolsAsync(model, staged, content, principal, cancellationToken)
            : await PublishPackageAsync(model, staged, content, principal, cancellationToken);
    }

    /// <summary>
    /// Discard a staged upload. Authenticated, owner-checked and refused in read-only
    /// mode, exactly as the other two steps are: the token is the only handle on an
    /// upload, so anyone who learned one - from a log, a screenshot, a shared screen -
    /// could otherwise make its owner upload the whole file again, and the deletion is a
    /// write to the store that read-only mode is meant to freeze.
    /// </summary>
    public async Task<ScreenResult<UploadViewModel>> CancelAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        string? id,
        string token,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        return Result(await CancelCoreAsync(user, key, id, token, apiKey, cancellationToken));
    }

    private async Task<UploadViewModel> CancelCoreAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        string? id,
        string token,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        var model = NewModel(user, id);

        if (IsReadOnlyMode) return Refuse(model, 401, ReadOnlyMessage);
        if (NothingCanAuthenticate) return Refuse(model, 401, NoApiKeyMessage);

        var principal = await _authenticator.AuthenticateAsync(user, key, apiKey, cancellationToken);
        if (principal == null) return RefuseKey(model, token);

        // Another user's token is indistinguishable from one that never existed, as it
        // is when submitting.
        if (!await _staging.RemoveAsync(token, principal.UserId, cancellationToken))
        {
            return Refuse(model, 404, UnknownTokenMessage);
        }

        model.Message = "The upload was discarded.";

        return model;
    }

    private async Task<UploadViewModel> PublishPackageAsync(
        UploadViewModel model,
        UploadPreview staged,
        Stream content,
        BaGetPrincipal principal,
        CancellationToken cancellationToken)
    {
        PackageIndexingResult result;
        // Decided before the push: afterwards every ID exists.
        var isNewId = !await _packages.ExistsAsync(staged.Id, cancellationToken);
        var refused = await RefuseIfBlockedAsync(model, content, cancellationToken);
        if (refused != null)
        {
            model.Staged = staged;
            return refused;
        }

        try
        {
            result = await _submissions.SubmitAsync(content, cancellationToken);
        }
        catch (Exception e) when (!(e is OperationCanceledException) && !(e is MaintenanceException))
        {
            // A failure the server owns: said in a sentence rather than as a stack trace,
            // and the upload is kept so it can be submitted again.
            _logger.LogError(e, "Could not publish the staged upload {Token}", staged.Token);

            model.Staged = staged;
            return Refuse(model, 500, "The server could not publish this package. Try again.");
        }

        // An accepted package claims its ID now, as it does on the push endpoint: it is
        // the pusher's even while it is being validated.
        // The package is already published when this runs, so a failure here must not
        // turn the page into a bare 500, and must not keep the staged upload alive.
        var ownershipRecorded = true;
        try
        {
            if (result == PackageIndexingResult.Success || result == PackageIndexingResult.Accepted)
            {
                await _authorization.OnPushedAsync(principal, staged.Id, isNewId, cancellationToken);
            }
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            _logger.LogError(e, "Could not record the ownership of {PackageId}", staged.Id);
            ownershipRecorded = false;
        }
        finally
        {
            await _staging.RemoveAsync(staged.Token, principal.UserId, cancellationToken);
        }

        switch (result)
        {
            case PackageIndexingResult.Success:
            case PackageIndexingResult.Accepted:
                // Only a package that is indexed can be linked to: an accepted one is
                // still being validated, so its page does not exist yet and may never.
                if (result == PackageIndexingResult.Success) model.Published = staged;

                model.Message = result == PackageIndexingResult.Accepted
                    ? $"{staged.Id} {staged.Version} was accepted and is being validated."
                    : $"{staged.Id} {staged.Version} was published.";

                if (!ownershipRecorded)
                {
                    model.Message += $" Recording you as the owner of {staged.Id} failed; ask an administrator to add you.";
                }

                return model;

            case PackageIndexingResult.PackageAlreadyExists:
                // This upload can never be published, so keeping it staged would leave a
                // file nobody could use.
                return Refuse(model, 409,
                    $"{staged.Id} {staged.Version} is already on this server, and this server does "
                    + "not replace a published version. Publish a new version instead.");

            default:
                return Refuse(model, 400, UnreadableMessage);
        }
    }

    private async Task<UploadViewModel> PublishSymbolsAsync(
        UploadViewModel model,
        UploadPreview staged,
        Stream? content,
        BaGetPrincipal principal,
        CancellationToken cancellationToken)
    {
        // Branched on the indexing result, not on a status code: a malformed symbol
        // package and one whose package is missing are told apart here, while the push
        // endpoint reports the first as 400 and every exception as 400 as well.
        var result = await _symbols.IndexAsync(content, cancellationToken);

        await _staging.RemoveAsync(staged.Token, principal.UserId, cancellationToken);

        switch (result)
        {
            case SymbolIndexingResult.Success:
                // No OnPushedAsync: symbols never claim an ID, exactly as on the symbol
                // endpoint, so a symbol upload confers no ownership.
                model.Published = staged;
                model.Message = $"The symbols for {staged.Id} {staged.Version} were published.";
                return model;

            case SymbolIndexingResult.PackageNotFound:
                return Refuse(model, 404, MissingSymbolTarget(staged));

            default:
                return Refuse(model, 400, InvalidSymbolPackageMessage);
        }
    }

    private static string Denied(string? packageId)
        => $"You may not publish '{packageId}'. Either someone else owns it, or it predates "
           + "this server's accounts and only an administrator may publish to it.";

    private static string MissingSymbolTarget(UploadPreview staged)
        => "Symbols can only be uploaded for a package version already on this server. "
           + $"Push {staged.Id} {staged.Version} first.";

    /// <summary>
    /// Everything the preview shows, read from the archive itself. Null when the file is
    /// not a package at all.
    /// </summary>
    private async Task<UploadPreview?> ReadOrNullAsync(Stream content, CancellationToken cancellationToken)
    {
        try
        {
            content.Position = 0;

            using var reader = new PackageArchiveReader(content, leaveStreamOpen: true);
            var nuspec = reader.NuspecReader;

            // The content decides which pipeline runs, not the file's name: a symbol
            // package says so in its own nuspec, so renaming a file cannot send it to a
            // pipeline its content does not belong to.
            var isSymbolPackage = nuspec
                .GetPackageTypes()
                .Any(t => string.Equals(t.Name, NuGetPackageType.SymbolsPackage.Name, StringComparison.OrdinalIgnoreCase));

            var staged = new UploadPreview
            {
                IsSymbolPackage = isSymbolPackage,
                Id = nuspec.GetId(),
                Version = nuspec.GetVersion().ToNormalizedString(),
            };

            if (isSymbolPackage)
            {
                staged.SymbolFiles = reader
                    .GetFiles()
                    .Where(f => Path.GetExtension(f).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return staged;
            }

            var metadata = reader.GetPackageMetadata();

            staged.Description = metadata.Description;
            staged.Authors = metadata.Authors ?? Array.Empty<string>();
            staged.LicenseUrl = metadata.LicenseUrlString;
            staged.License = nuspec.GetLicenseMetadata()?.License;
            staged.DependencyGroups = PackageDependencies.GroupByFramework(metadata.Dependencies);

            if (reader.HasReadme())
            {
                using var readme = await reader.GetReadmeAsync(cancellationToken);
                using var text = new StreamReader(readme);

                staged.Readme = ReadmeMarkdown.ToHtml(await text.ReadToEndAsync(cancellationToken));
            }

            return staged;
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            _logger.LogInformation(e, "An upload could not be read as a package");
            return null;
        }
        finally
        {
            if (content.CanSeek) content.Position = 0;
        }
    }

    /// <summary>
    /// Symbols carry no metadata of their own, so the preview reports the version they
    /// belong to - and says now, rather than after the upload is confirmed, when there is
    /// no such version to attach them to.
    /// </summary>
    private async Task DescribeSymbolTargetAsync(UploadPreview staged, CancellationToken cancellationToken)
    {
        if (!staged.IsSymbolPackage) return;
        if (!NuGetVersion.TryParse(staged.Version, out var version)) return;

        var target = await _packages.FindOrNullAsync(staged.Id, version, includeUnlisted: true, cancellationToken);

        staged.TargetPackageExists = target != null;
        staged.TargetPackageMessage = target == null ? MissingSymbolTarget(staged) : null;
    }

    /// <summary>
    /// A rejected API key on the confirm or discard step. The staged upload is untouched,
    /// so the page keeps its token and offers the key again instead of dropping back to
    /// step one and forcing the whole file to be uploaded again.
    /// </summary>
    private UploadViewModel RefuseKey(UploadViewModel model, string token)
    {
        if (!AccountsEnabled && !string.IsNullOrEmpty(token) && token.Length == 32 && token.All(Uri.IsHexDigit))
        {
            model.RetryToken = token;
        }

        return Refuse(model, 401, NotAuthenticatedMessage);
    }

    // 403 with the reason when the scanning policy of the repository refuses a package with these dependencies (the stream is rewound).
    private async Task<UploadViewModel?> RefuseIfBlockedAsync(UploadViewModel model, Stream package, CancellationToken cancellationToken)
    {
        if (!_scanGate.Enabled || !await _scanGate.BlocksDeploysAsync(cancellationToken)) return null;

        var components = NuGetScanComponents.TryRead(package);
        if (components == null) return null;

        try
        {
            await _scanGate.CheckDeployAsync(components, cancellationToken);

            return null;
        }
        catch (ScanBlockedException e)
        {
            return Refuse(model, 403, e.Message);
        }
    }

    // A refusal is a screen the form is shown again on, with the status the refusal carries and its sentence as the form's error; the rest is the screen.
    private static ScreenResult<UploadViewModel> Result(UploadViewModel model)
        => model.Failed
            ? ScreenResult<UploadViewModel>.Invalid(model, model.Message ?? string.Empty, model.StatusCode)
            : ScreenResult<UploadViewModel>.Ok(model, model.StatusCode == 200 ? null : model.StatusCode);

    private static UploadViewModel Refuse(UploadViewModel model, int statusCode, string message)
    {
        model.Failed = true;
        model.Message = message;
        model.StatusCode = statusCode;

        return model;
    }

    private UploadViewModel NewModel(ClaimsPrincipal user, string? id) => new()
    {
        Id = id,
        AccountsEnabled = AccountsEnabled,
        IsReadOnlyMode = IsReadOnlyMode,
        SignedIn = user.Identity?.IsAuthenticated == true,
        NothingCanAuthenticate = NothingCanAuthenticate,
    };
}
