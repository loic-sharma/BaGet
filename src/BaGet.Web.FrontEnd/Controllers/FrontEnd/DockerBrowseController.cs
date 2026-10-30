using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Docker;

namespace BaGet.Web.V3.Controllers.FrontEnd;

/// <summary>
/// The web pages of a Docker repository: the image list (<c>/docker-images</c>) and one image
/// (<c>/docker-images/{image}</c>, <c>?digest=</c> for a manifest). They replace the Razor Pages <c>DockerImages</c> and
/// <c>DockerImage</c>: same URLs, same repository rules (<see cref="RepositoryPageAttribute"/>: Read on the repository and,
/// for the image page, on the image; 404 in a repository that is not Docker's). Views are in <c>Views/DockerBrowse/</c>. The work is <see cref="BaGet.Web.Application.UseCases.DockerBrowseUseCase"/>, reached through the JSON twin
/// <see cref="DockerBrowseApiController"/> in process; this controller only renders what it answers.
/// </summary>
public sealed class DockerBrowseController : UiControllerBase
{
    /// <summary>The image list with a search box and the client commands.</summary>
    [Route("docker-images"), HttpGet, HttpHead, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Docker)]
    public async Task<IActionResult> Images(
        string? q,
        [Range(1, int.MaxValue)] int p = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest();

        return Render(await Api<DockerBrowseApiController, DockerImagesViewModel>(api => api.Images(q, p, cancellationToken)), "Images");
    }

    /// <summary>One image: tags, manifests, and with <c>?digest=</c> one manifest in full.</summary>
    [Route("docker-images/{**image}"), HttpGet, HttpHead, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Docker)]
    public async Task<IActionResult> Image(string? image, string? digest, string? artifactType, CancellationToken cancellationToken = default)
        => Render(await Api<DockerBrowseApiController, DockerImageViewModel>(api => api.Image(image, digest, artifactType, cancellationToken)), "Image");
}
