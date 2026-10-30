using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.Options;
using BaGet.Web.Application.Models.Maven;
using BaGet.Web.Application.Models.Packages;
using BaGet.Web.Application.Models.Upload;
using BaGet.Web.Application.Services;
using BaGet.Web.Application.Snippets;

namespace BaGet.Web.Application.UseCases;

/// <summary>
/// The screens of a Maven repository: the artifact list (<c>/maven-artifacts</c>) with the configuration that points Maven and Gradle at the
/// repository, one artifact (<c>/maven-artifacts/{group}/{artifact}/{version?}</c>) and the browser deployment (<c>/maven-upload</c>), whose
/// deploy goes through <see cref="IMavenRepository.DeployAsync"/>, the call <c>mvn deploy</c>'s uploads end in. Used by
/// <c>MavenBrowseApiController</c>, which the view controller calls in process.
/// </summary>
public sealed class MavenBrowseUseCase
{
    /// <summary>Artifacts per page.</summary>
    public const int ResultsPerPage = 20;

    internal const string ReadOnlyMessage = "This server is in read-only mode, so nothing can be uploaded.";
    internal const string SignInMessage = "Sign in to upload an artifact.";
    internal const string ApiKeyRejectedMessage = "That API key was not accepted.";
    internal const string NoFileMessage = "Choose a file or a POM to deploy.";

    private readonly IMavenRepository _maven;
    private readonly IUrlGenerator _urls;
    private readonly IRepositoryAccess _access;
    private readonly IRepositoryContext _repository;
    private readonly IOptionsSnapshot<BaGetOptions> _options;
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly UploadAuthenticator _authenticator;
    private readonly ManageAccess _manage;

    /// <summary>Creates the use case.</summary>
    public MavenBrowseUseCase(
        IMavenRepository maven,
        IUrlGenerator urls,
        IRepositoryAccess access,
        IRepositoryContext repository,
        IOptionsSnapshot<BaGetOptions> options,
        IOptions<AccountsOptions> accounts,
        UploadAuthenticator authenticator,
        ManageAccess manage)
    {
        _maven = maven ?? throw new ArgumentNullException(nameof(maven));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _manage = manage ?? throw new ArgumentNullException(nameof(manage));
    }

    /// <summary>The artifacts whose coordinates or POM text contain the search text (only those the visitor may open) and the client configuration.</summary>
    public async Task<ScreenResult<MavenArtifactsViewModel>> ArtifactsAsync(string? query, int pageIndex, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (pageIndex < 1)
        {
            return ScreenResult<MavenArtifactsViewModel>.Invalid(new MavenArtifactsViewModel(), new Dictionary<string, string[]> { ["p"] = new[] { "The page must be 1 or more." } }, 400);
        }

        // The listing names only the artifacts whose pages the visitor may open.
        var repository = _repository.Current;
        var result = await _maven.SearchAsync(
            query,
            (pageIndex - 1) * ResultsPerPage,
            ResultsPerPage,
            (name, token) => _access.CanReadPackageAsync(user, repository, name, token),
            cancellationToken);
        var settings = await _maven.GetSettingsAsync(cancellationToken);

        var kind = repository.Kind;
        var id = repository.Name;
        var url = _urls.GetMavenRepositoryUrl();
        var tabs = new List<InstallSnippet> { new() { Name = "settings.xml", Lines = MavenSnippets.SettingsXml(id) } };
        if (kind == RepositoryKind.Remote) tabs.Add(new InstallSnippet { Name = "Mirror of Maven Central (settings.xml)", Lines = MavenSnippets.Mirror(id, url) });
        if (kind != RepositoryKind.Remote) tabs.Add(new InstallSnippet { Name = "Deploy (pom.xml)", Lines = MavenSnippets.DistributionManagement(id, url) });

        tabs.Add(new InstallSnippet { Name = "Resolve (pom.xml)", Lines = MavenSnippets.Repositories(id, url, settings.Policy != MavenPolicy.Snapshots, settings.Policy != MavenPolicy.Releases) });
        tabs.Add(new InstallSnippet { Name = "Gradle", Lines = MavenSnippets.GradleRepository(url) });

        return ScreenResult<MavenArtifactsViewModel>.Ok(new MavenArtifactsViewModel
        {
            Query = query,
            PageIndex = pageIndex,
            Result = result,
            Settings = settings,
            RepositoryUrl = url,
            Kind = kind,
            RepositoryName = id,
            ConfigurationTabs = tabs,
        });
    }

    /// <summary>
    /// One artifact: its versions, and for the chosen version its files, POM and the dependency to copy. Not found when the coordinates or the
    /// version are not valid Maven names; an artifact or version that does not exist is the page that explains it, with status 404.
    /// </summary>
    public async Task<ScreenResult<MavenArtifactViewModel>> ArtifactAsync(string? group, string? artifact, string? version, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!MavenNames.IsValidGroupId(group) || !MavenNames.IsValidArtifactId(artifact)) return ScreenResult<MavenArtifactViewModel>.NotFound();
        if (version != null && !MavenNames.IsValidVersion(version)) return ScreenResult<MavenArtifactViewModel>.NotFound();

        var repositoryUrl = _urls.GetMavenRepositoryUrl();
        var repositoryName = _repository.Current.Name;

        var info = await _maven.GetArtifactAsync(group!, artifact!, cancellationToken);
        if (info == null)
        {
            // The page explains what is missing, with the status a link checker expects.
            return Explained(new MavenArtifactViewModel { Group = group, Artifact = artifact, Version = version, RepositoryUrl = repositoryUrl, RepositoryName = repositoryName, StatusCode = 404 });
        }

        var chosen = version == null ? info.Latest : info.Versions.FirstOrDefault(v => v.Version == version);
        if (chosen == null)
        {
            return Explained(new MavenArtifactViewModel { Group = group, Artifact = artifact, Version = version, Info = info, RepositoryUrl = repositoryUrl, RepositoryName = repositoryName, StatusCode = 404 });
        }

        var selected = await _maven.GetVersionAsync(info.GroupId, info.ArtifactId, chosen.Version, cancellationToken);
        IReadOnlyList<InstallSnippet> installTabs = Array.Empty<InstallSnippet>();
        if (selected != null)
        {
            installTabs = new[]
            {
                new InstallSnippet { Name = "Maven", Lines = MavenSnippets.Dependency(info.GroupId, info.ArtifactId, chosen.Version) },
                new InstallSnippet { Name = "Gradle", Lines = new[] { MavenSnippets.GradleDependency(info.GroupId, info.ArtifactId, chosen.Version) } },
                new InstallSnippet
                {
                    Name = "Check",
                    Prefix = ">",
                    Lines = new[] { MavenSnippets.GetCommand(info.GroupId, info.ArtifactId, chosen.Version, repositoryName, repositoryUrl) },
                },
            };
        }

        // Owners and the manage page belong to a repository that holds artifacts of its own (a local one).
        var (owners, canManage) = await _manage.OwnersAndManageAsync(user, info.Name, cancellationToken);

        return Explained(new MavenArtifactViewModel
        {
            Group = group,
            Artifact = artifact,
            Version = version,
            Info = info,
            Selected = selected,
            RepositoryUrl = repositoryUrl,
            RepositoryName = repositoryName,
            InstallTabs = installTabs,
            Owners = owners,
            CanManage = canManage,
        });
    }

    /// <summary>The upload page before anything was posted.</summary>
    public async Task<ScreenResult<MavenUploadViewModel>> UploadFormAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
        => Result(Build(user, await _maven.GetSettingsAsync(cancellationToken), null, null, false, null, null, 200));

    /// <summary>Deploys the chosen files under the given coordinates. A refusal is the form again (with the coordinates posted), with the status it carries.</summary>
    public async Task<ScreenResult<MavenUploadViewModel>> DeployAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        string? groupId,
        string? artifactId,
        string? version,
        string? packaging,
        string? classifier,
        UploadedFile? file,
        UploadedFile? pom,
        string? apiKey,
        CancellationToken cancellationToken)
        => Result(await DeployCoreAsync(user, key, groupId, artifactId, version, packaging, classifier, file, pom, apiKey, cancellationToken));

    private async Task<MavenUploadViewModel> DeployCoreAsync(
        ClaimsPrincipal user,
        BaGetPrincipal? key,
        string? groupId,
        string? artifactId,
        string? version,
        string? packaging,
        string? classifier,
        UploadedFile? file,
        UploadedFile? pom,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        var settings = await _maven.GetSettingsAsync(cancellationToken);
        var posted = new MavenDeployment { GroupId = groupId, ArtifactId = artifactId, Version = version, Packaging = packaging, Classifier = classifier };
        MavenUploadViewModel Refuse(int status, string message) => Build(user, settings, posted, message, true, null, null, status);

        if (_options.Value.IsReadOnlyMode) return Refuse(401, ReadOnlyMessage);
        if (UploadAvailability.NothingCanAuthenticate(_options.Value, _accounts.Value))
        {
            return Refuse(401, "This server has no API key configured and no accounts, so the browser upload is closed.");
        }

        var principal = await _authenticator.AuthenticateAsync(user, key, apiKey, cancellationToken);
        if (principal == null) return Refuse(401, _accounts.Value.Enabled ? SignInMessage : ApiKeyRejectedMessage);

        var hasFile = file != null && file.Length > 0;
        var hasPom = pom != null && pom.Length > 0;
        if (!hasFile && !hasPom) return Refuse(400, NoFileMessage);

        await using var main = hasFile ? file!.OpenReadStream() : null;
        await using var pomStream = hasPom ? pom!.OpenReadStream() : null;
        MavenResult result;
        try
        {
            result = await _maven.DeployAsync(
                principal,
                new MavenDeployment
                {
                    GroupId = groupId,
                    ArtifactId = artifactId,
                    Version = version,
                    Packaging = packaging,
                    Classifier = classifier,
                    File = main,
                    Pom = pomStream,
                },
                cancellationToken);
        }
        catch (ScanBlockedException e)
        {
            // The scanning policy of the repository refuses an artifact with known vulnerabilities.
            return Refuse(403, e.Message);
        }

        if (!result.Succeeded)
        {
            return Refuse(StatusOf(result.Status), result.Message ?? "The artifact was not deployed.");
        }

        return Build(user, settings, posted, result.Message ?? "Deployed.", false, result.Subject, version?.Trim(), 200);
    }

    // A page that explains a missing artifact carries 404 as its status (the screen is still shown).
    private static ScreenResult<MavenArtifactViewModel> Explained(MavenArtifactViewModel model)
        => ScreenResult<MavenArtifactViewModel>.Ok(model, model.StatusCode == 200 ? null : model.StatusCode);

    private static ScreenResult<MavenUploadViewModel> Result(MavenUploadViewModel model)
        => model.Failed
            ? ScreenResult<MavenUploadViewModel>.Invalid(model, model.Message ?? string.Empty, model.StatusCode)
            : ScreenResult<MavenUploadViewModel>.Ok(model, model.StatusCode == 200 ? null : model.StatusCode);

    private static int StatusOf(MavenStatus status) => status switch
    {
        MavenStatus.Unauthorized => 401,
        MavenStatus.Forbidden => 403,
        MavenStatus.NotFound => 404,
        MavenStatus.Conflict => 409,
        MavenStatus.TooLarge => 413,
        _ => 400,
    };

    private MavenUploadViewModel Build(
        ClaimsPrincipal user,
        MavenRepositorySettings settings,
        MavenDeployment? posted,
        string? message,
        bool failed,
        string? deployedName,
        string? deployedVersion,
        int status)
        => new()
        {
            AccountsEnabled = _accounts.Value.Enabled,
            IsReadOnlyMode = _options.Value.IsReadOnlyMode,
            SignedIn = user.Identity?.IsAuthenticated == true,
            NothingCanAuthenticate = UploadAvailability.NothingCanAuthenticate(_options.Value, _accounts.Value),
            RepositoryUrl = _urls.GetMavenRepositoryUrl(),
            RepositoryName = _repository.Current.Name,
            Settings = settings,
            Message = message,
            Failed = failed,
            DeployedName = deployedName,
            DeployedVersion = deployedVersion,
            Posted = posted,
            StatusCode = status,
        };
}
