using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Maven;

namespace BaGet.Web.V3.Controllers.FrontEnd;

/// <summary>
/// The web pages of a Maven repository: the artifact list (<c>/maven-artifacts</c>), one artifact
/// (<c>/maven-artifacts/{group}/{artifact}/{version?}</c>) and the browser deployment (<c>/maven-upload</c>). They replace the Razor
/// Pages <c>MavenArtifacts</c>, <c>MavenArtifact</c> and <c>MavenUpload</c>: same URLs, same repository rules
/// (<see cref="RepositoryPageAttribute"/>: Read on the repository, on <c>group:artifact</c> for the artifact page, the right to
/// publish for the upload; 404 in a repository that is not Maven's). Views are in <c>Views/MavenBrowse/</c>. The work is <see cref="BaGet.Web.Application.UseCases.MavenBrowseUseCase"/>, reached through the JSON twin
/// <see cref="MavenBrowseApiController"/> in process; this controller only renders what it answers.
/// </summary>
public sealed class MavenBrowseController : UiControllerBase
{
    /// <summary>The artifact list with a search box and the client configuration.</summary>
    [Route("maven-artifacts"), HttpGet, HttpHead, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Maven)]
    public async Task<IActionResult> Artifacts(
        string? q,
        [Range(1, int.MaxValue)] int p = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest();

        return Render(await Api<MavenBrowseApiController, MavenArtifactsViewModel>(api => api.Artifacts(q, p, cancellationToken)), "Artifacts");
    }

    /// <summary>One artifact: versions, files, POM, the dependency to copy.</summary>
    [Route("maven-artifacts/{group}/{artifact}/{version?}"), HttpGet, HttpHead, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Maven)]
    public async Task<IActionResult> Artifact(string? group, string? artifact, string? version, CancellationToken cancellationToken = default)
        => Render(await Api<MavenBrowseApiController, MavenArtifactViewModel>(api => api.Artifact(group, artifact, version, cancellationToken)), "Artifact");

    /// <summary>The upload form.</summary>
    [Route("maven-upload"), HttpGet, HttpHead, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Maven, Upload = true)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
        => Render(await Api<MavenBrowseApiController, MavenUploadViewModel>(api => api.Upload(cancellationToken)), "Upload");

    /// <summary>Deploys the chosen files under the given coordinates.</summary>
    [Route("maven-upload"), HttpPost, PageHandler]
    [RepositoryPage(Format = RepositoryFormat.Maven, Upload = true)]
    [UploadSizeLimit]
    public async Task<IActionResult> UploadPost(
        string? groupId,
        string? artifactId,
        string? version,
        string? packaging,
        string? classifier,
        IFormFile? file,
        IFormFile? pom,
        string? apiKey,
        CancellationToken cancellationToken)
        => Render(
            await Api<MavenBrowseApiController, MavenUploadViewModel>(api => api.UploadPost(groupId, artifactId, version, packaging, classifier, file, pom, apiKey, cancellationToken)),
            "Upload");
}
