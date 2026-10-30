using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using BaGet.Web.V3.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Maven;
using BaGet.Web.Application.UseCases;

namespace BaGet.Web.V3.Controllers.UI;

/// <summary>
/// The JSON twin of the Maven repository screens: <c>GET /api/ui/maven-artifacts</c> (list and client configuration, <c>q</c> and <c>p</c>),
/// <c>GET /api/ui/maven-artifacts/{group}/{artifact}/{version?}</c> (one artifact), <c>GET /api/ui/maven-upload</c> (the form's state) and
/// <c>POST /api/ui/maven-upload</c> (multipart: <c>groupId</c>, <c>artifactId</c>, <c>version</c>, <c>packaging</c>, <c>classifier</c>, <c>file</c>, <c>pom</c> and,
/// while accounts are off, <c>apiKey</c>). Also under <c>/api/ui/r/{repo}/...</c>, with the pages' repository rules (<see cref="RepositoryPageAttribute"/>: Read on
/// the repository and on <c>group:artifact</c>, the right to publish for the upload, 404 in a repository that is not Maven's). An artifact or version that does not
/// exist answers <c>404</c> with the explaining screen; a refused deployment answers its status (401, 403, 404, 409, 413) with the form's state. An API key in the
/// request authenticates the uploader within its scopes. <c>MavenBrowseController</c> calls it in process.
/// </summary>
[Route("api/ui")]
public sealed class MavenBrowseApiController : ApiControllerBase
{
    private readonly MavenBrowseUseCase _maven;

    /// <summary>Creates the controller.</summary>
    public MavenBrowseApiController(MavenBrowseUseCase maven)
    {
        _maven = maven ?? throw new ArgumentNullException(nameof(maven));
    }

    /// <summary>The artifact list with a search box and the client configuration.</summary>
    [HttpGet("maven-artifacts"), UiApi("MavenBrowse", "Artifacts")]
    [RepositoryPage(Format = RepositoryFormat.Maven)]
    public async Task<ActionResult<ScreenResult<MavenArtifactsViewModel>>> Artifacts(string? q, int p = 1, CancellationToken cancellationToken = default)
        => Respond(await _maven.ArtifactsAsync(q, p, User, cancellationToken));

    /// <summary>One artifact: versions, files, POM, the dependency to copy.</summary>
    [HttpGet("maven-artifacts/{group}/{artifact}/{version?}"), UiApi("MavenBrowse", "Artifact")]
    [RepositoryPage(Format = RepositoryFormat.Maven)]
    public async Task<ActionResult<ScreenResult<MavenArtifactViewModel>>> Artifact(string? group, string? artifact, string? version, CancellationToken cancellationToken = default)
        => Respond(await _maven.ArtifactAsync(group, artifact, version, User, cancellationToken));

    /// <summary>The upload form's state.</summary>
    [HttpGet("maven-upload"), UiApi("MavenBrowse", "Upload")]
    [RepositoryPage(Format = RepositoryFormat.Maven, Upload = true)]
    public async Task<ActionResult<ScreenResult<MavenUploadViewModel>>> Upload(CancellationToken cancellationToken)
        => Respond(await _maven.UploadFormAsync(User, cancellationToken));

    /// <summary>Deploys the chosen files under the given coordinates.</summary>
    [HttpPost("maven-upload"), UiApi("MavenBrowse", "Upload")]
    [RepositoryPage(Format = RepositoryFormat.Maven, Upload = true)]
    [UploadSizeLimit]
    public async Task<ActionResult<ScreenResult<MavenUploadViewModel>>> UploadPost(
        string? groupId,
        string? artifactId,
        string? version,
        string? packaging,
        string? classifier,
        IFormFile? file,
        IFormFile? pom,
        string? apiKey,
        CancellationToken cancellationToken)
        => Respond(await _maven.DeployAsync(User, KeyCaller, groupId, artifactId, version, packaging, classifier, file.ToUploadedFile(), pom.ToUploadedFile(), apiKey, cancellationToken));
}
