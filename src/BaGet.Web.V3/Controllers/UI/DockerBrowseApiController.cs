using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Docker;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.V3.Controllers.UI;

/// <summary>
/// The JSON twin of the Docker repository screens: <c>GET /api/ui/docker-images</c> (list and client commands, <c>q</c> and <c>p</c>) and
/// <c>GET /api/ui/docker-images/{image}</c> (one image; <c>digest</c> for a manifest in full, <c>artifactType</c> to narrow the referrers). Also under
/// <c>/api/ui/r/{repo}/...</c>, with the pages' repository rules (<see cref="RepositoryPageAttribute"/>: Read on the repository and the image, 404 in a repository
/// that is not Docker's). An image or manifest that does not exist answers <c>404</c> with the explaining screen. <c>DockerBrowseController</c> calls it in process.
/// </summary>
[Route("api/ui")]
public sealed class DockerBrowseApiController : ApiControllerBase
{
    private readonly DockerBrowseUseCase _docker;

    /// <summary>Creates the controller.</summary>
    public DockerBrowseApiController(DockerBrowseUseCase docker)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
    }

    /// <summary>The image list with a search box and the client commands.</summary>
    [HttpGet("docker-images"), UiApi("DockerBrowse", "Images")]
    [RepositoryPage(Format = RepositoryFormat.Docker)]
    public async Task<ActionResult<ScreenResult<DockerImagesViewModel>>> Images(string? q, int p = 1, CancellationToken cancellationToken = default)
        => Respond(await _docker.ImagesAsync(q, p, User, cancellationToken));

    /// <summary>One image: tags, manifests, and with <c>digest</c> one manifest in full.</summary>
    [HttpGet("docker-images/{**image}"), UiApi("DockerBrowse", "Image")]
    [RepositoryPage(Format = RepositoryFormat.Docker)]
    public async Task<ActionResult<ScreenResult<DockerImageViewModel>>> Image(string? image, string? digest, string? artifactType, CancellationToken cancellationToken = default)
        => Respond(await _docker.ImageAsync(image, digest, artifactType, User, cancellationToken));
}
