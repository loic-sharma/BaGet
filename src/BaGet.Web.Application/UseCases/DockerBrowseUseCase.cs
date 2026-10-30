using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using BaGet.Web.Application.Models.Docker;
using BaGet.Web.Application.Models.Packages;
using BaGet.Web.Application.Snippets;

namespace BaGet.Web.Application.UseCases;

/// <summary>
/// The screens of a Docker repository: the image list (<c>/docker-images</c>) with the commands that point Docker at the registry, and one image
/// (<c>/docker-images/{image}</c>; with a digest, one manifest in full). Used by <c>DockerBrowseApiController</c>, which the view controller calls in process.
/// </summary>
public sealed class DockerBrowseUseCase
{
    /// <summary>Images per page.</summary>
    public const int ResultsPerPage = 20;

    private readonly IOciRegistry _registry;
    private readonly IUrlGenerator _urls;
    private readonly IRepositoryAccess _access;
    private readonly IRepositoryContext _repository;
    private readonly ManageAccess _manage;

    /// <summary>Creates the use case.</summary>
    public DockerBrowseUseCase(IOciRegistry registry, IUrlGenerator urls, IRepositoryAccess access, IRepositoryContext repository, ManageAccess manage)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _manage = manage ?? throw new ArgumentNullException(nameof(manage));
    }

    /// <summary>The images whose name contains the text (only those the visitor may read) and the commands that point Docker at the registry.</summary>
    public async Task<ScreenResult<DockerImagesViewModel>> ImagesAsync(string? query, int pageIndex, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (pageIndex < 1)
        {
            return ScreenResult<DockerImagesViewModel>.Invalid(new DockerImagesViewModel(), new Dictionary<string, string[]> { ["p"] = new[] { "The page must be 1 or more." } }, 400);
        }

        var repository = _repository.Current;
        var result = await _registry.ListImagesAsync(
            query,
            (pageIndex - 1) * ResultsPerPage,
            ResultsPerPage,
            (name, token) => _access.CanReadPackageAsync(user, repository, name, token),
            cancellationToken);

        var (host, isHttp) = _urls.GetOciRegistryHost();
        var repositoryName = repository.Name;
        var isRemote = repository.Kind == RepositoryKind.Remote;
        var tabs = new List<InstallSnippet>
        {
            new() { Name = "Login", Prefix = ">", Lines = new[] { DockerSnippets.Login(host) } },
            new() { Name = "Pull", Prefix = ">", Lines = new[] { DockerSnippets.Pull(host, repositoryName, "team/app", "latest") } },
        };
        if (isRemote)
        {
            // A cache of Docker Hub serves the official images as single names: nginx is library/nginx there.
            tabs.Add(new InstallSnippet { Name = "Pull an official image", Prefix = ">", Lines = new[] { DockerSnippets.Pull(host, repositoryName, "nginx", "latest") } });
        }
        else
        {
            tabs.Add(new InstallSnippet { Name = "Push", Prefix = ">", Lines = DockerSnippets.Push(host, repositoryName, "team/app") });
            tabs.Add(new InstallSnippet { Name = "Multi-platform", Prefix = ">", Lines = new[] { DockerSnippets.BuildxPush(host, repositoryName, "team/app") } });
        }

        if (isHttp) tabs.Add(new InstallSnippet { Name = "Plain http (daemon.json)", Lines = DockerSnippets.InsecureRegistry(host) });

        return ScreenResult<DockerImagesViewModel>.Ok(new DockerImagesViewModel
        {
            Query = query,
            PageIndex = pageIndex,
            Result = result,
            Host = host,
            IsHttp = isHttp,
            RepositoryName = repositoryName,
            IsRemote = isRemote,
            IsVirtual = repository.Kind == RepositoryKind.Virtual,
            CommandTabs = tabs,
        });
    }

    /// <summary>
    /// One image: tags, manifests and, with a digest, one manifest in full. Not found when the image name or the digest is not valid; an image or
    /// manifest that does not exist is the page that explains it, with status 404.
    /// </summary>
    public async Task<ScreenResult<DockerImageViewModel>> ImageAsync(string? image, string? digest, string? artifactType, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image)) return ScreenResult<DockerImageViewModel>.NotFound();
        if (digest != null && !OciNames.IsValidDigest(digest)) return ScreenResult<DockerImageViewModel>.NotFound();

        var repository = _repository.Current;
        var repositoryName = repository.Name;
        var isRemote = repository.Kind == RepositoryKind.Remote;
        var registryUrl = $"{_urls.GetOciRegistryUrl()}{repositoryName}/{image}/";
        ScreenResult<DockerImageViewModel> Missing(OciImageInfo? info) => ScreenResult<DockerImageViewModel>.Ok(
            new DockerImageViewModel
            {
                Image = image,
                Digest = digest,
                ArtifactType = artifactType,
                Info = info,
                RepositoryName = repositoryName,
                IsRemote = isRemote,
                RegistryUrl = registryUrl,
                StatusCode = 404,
            },
            404);

        var info = await _registry.GetImageAsync(image!, cancellationToken);
        if (info == null)
        {
            // The page explains what is missing, with the status a link checker expects.
            return Missing(null);
        }

        OciManifestDetail? selected = null;
        if (digest != null)
        {
            selected = await _registry.GetManifestDetailAsync(image!, digest, cancellationToken);
            if (selected == null) return Missing(info);
        }

        IReadOnlyList<OciDescriptor> referrers = Array.Empty<OciDescriptor>();
        if (selected != null)
        {
            referrers = await _registry.GetReferrersAsync(image!, selected.Summary.Digest, string.IsNullOrWhiteSpace(artifactType) ? null : artifactType.Trim(), cancellationToken) ?? Array.Empty<OciDescriptor>();
        }

        var (host, _) = _urls.GetOciRegistryHost();
        var tag = selected?.Summary.Tags.FirstOrDefault() ?? info.Tags.FirstOrDefault()?.Name;
        var tabs = new List<InstallSnippet>();
        if (tag != null) tabs.Add(new InstallSnippet { Name = "Pull by tag", Prefix = ">", Lines = new[] { DockerSnippets.Pull(host, repositoryName, image!, tag: tag) } });
        var pullDigest = selected?.Summary.Digest ?? info.Tags.FirstOrDefault()?.Digest ?? info.Manifests.FirstOrDefault()?.Digest;
        if (pullDigest != null) tabs.Add(new InstallSnippet { Name = "Pull by digest", Prefix = ">", Lines = new[] { DockerSnippets.Pull(host, repositoryName, image!, digest: pullDigest) } });
        if (!isRemote) tabs.Add(new InstallSnippet { Name = "Push", Prefix = ">", Lines = DockerSnippets.Push(host, repositoryName, image!) });

        return ScreenResult<DockerImageViewModel>.Ok(new DockerImageViewModel
        {
            Image = image,
            Digest = digest,
            ArtifactType = artifactType,
            Info = info,
            Selected = selected,
            Referrers = referrers,
            Host = host,
            RepositoryName = repositoryName,
            IsRemote = isRemote,
            RegistryUrl = registryUrl,
            PullTabs = tabs,
            CanManage = await _manage.CanManageImageAsync(user, repository, image!, cancellationToken),
        });
    }
}
