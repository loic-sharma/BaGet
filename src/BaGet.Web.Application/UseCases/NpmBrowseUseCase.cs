using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.Options;
using BaGet.Web.Application.Models.Npm;
using BaGet.Web.Application.Models.Packages;
using BaGet.Web.Application.Models.Upload;
using BaGet.Web.Application.Services;
using BaGet.Web.Application.Snippets;

namespace BaGet.Web.Application.UseCases;

/// <summary>
/// The screens of an npm repository: the package list (<c>/npm-packages</c>), one package (<c>/npm-packages/{name}</c>) and the browser upload
/// (<c>/npm-upload</c>), whose publish goes through <see cref="INpmRegistry.PublishTarballAsync"/>, the call <c>npm publish</c> ends in. Used by
/// <c>NpmBrowseApiController</c>, which the view controller calls in process.
/// </summary>
public sealed class NpmBrowseUseCase
{
    /// <summary>Packages per page.</summary>
    public const int ResultsPerPage = 20;

    internal const string ReadOnlyMessage = "This server is in read-only mode, so nothing can be uploaded.";
    internal const string SignInMessage = "Sign in to upload a package.";
    internal const string ApiKeyRejectedMessage = "That API key was not accepted.";
    internal const string NoFileMessage = "Choose the .tgz file that npm pack made.";

    private readonly INpmRegistry _registry;
    private readonly IUrlGenerator _urls;
    private readonly IRepositoryAccess _access;
    private readonly IRepositoryContext _repository;
    private readonly IOptionsSnapshot<BaGetOptions> _options;
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly UploadAuthenticator _authenticator;
    private readonly ManageAccess _manage;

    /// <summary>Creates the use case.</summary>
    public NpmBrowseUseCase(
        INpmRegistry registry,
        IUrlGenerator urls,
        IRepositoryAccess access,
        IRepositoryContext repository,
        IOptionsSnapshot<BaGetOptions> options,
        IOptions<AccountsOptions> accounts,
        UploadAuthenticator authenticator,
        ManageAccess manage)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _manage = manage ?? throw new ArgumentNullException(nameof(manage));
    }

    /// <summary>The packages whose name contains the text, listed only when the visitor may open their pages.</summary>
    public async Task<ScreenResult<NpmPackagesViewModel>> PackagesAsync(string? query, int pageIndex, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (pageIndex < 1)
        {
            return ScreenResult<NpmPackagesViewModel>.Invalid(new NpmPackagesViewModel(), new Dictionary<string, string[]> { ["p"] = new[] { "The page must be 1 or more." } }, 400);
        }

        // The listing names only the packages whose pages the visitor may open.
        var repository = _repository.Current;
        var result = await _registry.SearchAsync(
            query,
            (pageIndex - 1) * ResultsPerPage,
            ResultsPerPage,
            (name, token) => _access.CanReadPackageAsync(user, repository, name, token),
            cancellationToken);

        return ScreenResult<NpmPackagesViewModel>.Ok(
            new NpmPackagesViewModel { Query = query, PageIndex = pageIndex, Result = result, RegistryUrl = _urls.GetNpmRegistryUrl() });
    }

    /// <summary>
    /// One package: readme, versions, dist-tags, owners and install commands. Not found when the name is not a valid npm name; a package
    /// that does not exist is the page that explains it, with status 404.
    /// </summary>
    public async Task<ScreenResult<NpmPackageViewModel>> PackageAsync(string? id, string? requestedVersion, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!NpmNames.TryParseLookup(id, out var name, out _)) return ScreenResult<NpmPackageViewModel>.NotFound();

        var registryUrl = _urls.GetNpmRegistryUrl();
        var package = await _registry.GetPackageAsync(name, cancellationToken);
        if (package == null)
        {
            // The page explains what is missing, with the status a link checker expects.
            return Explained(new NpmPackageViewModel { Id = id, RequestedVersion = requestedVersion, RegistryUrl = registryUrl, StatusCode = 404 });
        }

        var selected = (requestedVersion == null ? null : package.Versions.FirstOrDefault(v => v.Version == requestedVersion))
                       ?? package.Latest;
        if (selected == null)
        {
            return Explained(new NpmPackageViewModel { Id = id, RequestedVersion = requestedVersion, RegistryUrl = registryUrl, Package = package });
        }

        var readme = ReadmeMarkdown.ToHtml(selected.Readme);
        var selectedTags = package.DistTags.Where(t => t.Value == selected.Version).Select(t => t.Key).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var installTabs = NpmInstallSnippets.Build(package.Name, selected.Version, selectedTags.FirstOrDefault(t => t != "latest"), registryUrl);

        // Owners and the manage page belong to a repository that holds packages of its own; a remote one caches an upstream's and a
        // virtual one holds none (it is managed where the package is stored).
        var (owners, canManage) = await _manage.OwnersAndManageAsync(user, package.Name, cancellationToken);

        return Explained(new NpmPackageViewModel
        {
            Id = id,
            RequestedVersion = requestedVersion,
            RegistryUrl = registryUrl,
            Package = package,
            Selected = selected,
            Readme = readme,
            SelectedTags = selectedTags,
            InstallTabs = installTabs,
            Owners = owners,
            CanManage = canManage,
        });
    }

    /// <summary>The upload page before anything was posted.</summary>
    public ScreenResult<NpmUploadViewModel> UploadForm(ClaimsPrincipal user) => Result(Build(user, null, false, null, 200));

    /// <summary>
    /// Publishes the chosen tarball with the dist-tag <paramref name="tag"/> (default <c>latest</c>). A refusal is the form again, with the status it carries.
    /// </summary>
    public async Task<ScreenResult<NpmUploadViewModel>> PublishAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        UploadedFile? package,
        string? tag,
        string? apiKey,
        CancellationToken cancellationToken)
        => Result(await PublishCoreAsync(user, key, package, tag, apiKey, cancellationToken));

    private async Task<NpmUploadViewModel> PublishCoreAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        UploadedFile? package,
        string? tag,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (_options.Value.IsReadOnlyMode) return Refuse(user, 401, ReadOnlyMessage);
        if (UploadAvailability.NothingCanAuthenticate(_options.Value, _accounts.Value))
        {
            return Refuse(user, 401, "This server has no API key configured and no accounts, so the browser upload is closed.");
        }

        var principal = await _authenticator.AuthenticateAsync(user, key, apiKey, cancellationToken);
        if (principal == null) return Refuse(user, 401, _accounts.Value.Enabled ? SignInMessage : ApiKeyRejectedMessage);

        if (package == null || package.Length == 0) return Refuse(user, 400, NoFileMessage);

        // The registry reads the file twice (its package.json, then the bytes), so it must be seekable.
        using var upload = package.OpenReadStream();
        using var copy = upload.CanSeek ? null : await upload.AsTemporaryFileStreamAsync(cancellationToken);
        var content = copy ?? upload;

        var chosenTag = string.IsNullOrWhiteSpace(tag) ? "latest" : tag.Trim();
        NpmResult result;
        try
        {
            result = await _registry.PublishTarballAsync(principal, content, chosenTag, cancellationToken);
        }
        catch (ScanBlockedException e)
        {
            // The scanning policy of the repository refuses a package with known vulnerabilities.
            return Refuse(user, 403, e.Message);
        }

        if (!result.Succeeded)
        {
            return Refuse(user, StatusOf(result.Status), result.Message ?? "The package was not published.");
        }

        return Build(user, result.Message ?? "Published.", false, result.Subject, 200);
    }

    // A page that explains a missing package carries 404 as its status (the screen is still shown).
    private static ScreenResult<NpmPackageViewModel> Explained(NpmPackageViewModel model)
        => ScreenResult<NpmPackageViewModel>.Ok(model, model.StatusCode == 200 ? null : model.StatusCode);

    private static ScreenResult<NpmUploadViewModel> Result(NpmUploadViewModel model)
        => model.Failed
            ? ScreenResult<NpmUploadViewModel>.Invalid(model, model.Message ?? string.Empty, model.StatusCode)
            : ScreenResult<NpmUploadViewModel>.Ok(model, model.StatusCode == 200 ? null : model.StatusCode);

    private static int StatusOf(NpmStatus status) => status switch
    {
        NpmStatus.Unauthorized => 401,
        NpmStatus.Forbidden => 403,
        NpmStatus.NotFound => 404,
        NpmStatus.Conflict => 409,
        _ => 400,
    };

    private NpmUploadViewModel Refuse(ClaimsPrincipal user, int status, string message) => Build(user, message, true, null, status);

    private NpmUploadViewModel Build(ClaimsPrincipal user, string? message, bool failed, string? publishedName, int status)
    {
        var registryUrl = _urls.GetNpmRegistryUrl();

        return new NpmUploadViewModel
        {
            AccountsEnabled = _accounts.Value.Enabled,
            IsReadOnlyMode = _options.Value.IsReadOnlyMode,
            SignedIn = user.Identity?.IsAuthenticated == true,
            NothingCanAuthenticate = UploadAvailability.NothingCanAuthenticate(_options.Value, _accounts.Value),
            RegistryUrl = registryUrl,
            PublishLines = NpmInstallSnippets.PublishLines(registryUrl),
            Message = message,
            Failed = failed,
            PublishedName = publishedName,
            StatusCode = status,
        };
    }
}
