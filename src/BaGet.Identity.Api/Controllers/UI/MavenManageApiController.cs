using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.UseCases.Packages;
using BaGet.Identity.Models.Packages;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the Maven artifact management screen: <c>GET /api/ui/maven-manage/{group}/{artifact}?version=</c> and, for each handler
/// of the page, <c>POST /api/ui/maven-manage/{group}/{artifact}/{handler-in-kebab-case}</c> with a JSON body (also under
/// <c>/api/ui/r/{repo}/…</c>). Interactive session only (403 over HTTP to an API key and to an anonymous caller; the web screen calls it in process). <c>MavenManageController</c> calls it in process and renders the view.
/// </summary>
[Route("api/ui/maven-manage/{group}/{artifact}")]
[RepositoryPage(Format = RepositoryFormat.Maven, NotOnVirtual = true, NotOnRemote = true)]
public sealed class MavenManageApiController : AccountApiControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    private MavenManageUseCase UseCase => HttpContext.RequestServices.GetRequiredService<MavenManageUseCase>();

    /// <summary>The coordinates / repository-kind / ownership check that opens every handler (see <see cref="PackageManageApiController.EnterAsync"/>).</summary>
    [NonAction]
    public Task<ScreenResult<MavenManageViewModel>?> EnterAsync(string? group, string? artifact, string? version, string? handler, bool isWrite, CancellationToken cancellationToken)
        => UseCase.EnterAsync(User, group, artifact, version, handler, isWrite, cancellationToken);

    /// <summary>The screen.</summary>
    [HttpGet, UiApi("MavenManage", "Index")]
    public async Task<ActionResult<ScreenResult<MavenManageViewModel>>> Get(string? group, string? artifact, [FromQuery] string? version, CancellationToken cancellationToken)
        => Respond(await UseCase.GetAsync(User, group, artifact, version, cancellationToken));

    /// <summary>Adds an owner.</summary>
    [HttpPost("add-owner"), UiApi("MavenManage", "Index", Handler = "AddOwner")]
    public async Task<ActionResult<ScreenResult<MavenManageViewModel>>> AddOwner(string? group, string? artifact, [FromBody] MavenManageForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.AddOwnerAsync(User, group, artifact, form?.Version, form?.OwnerName, cancellationToken));

    /// <summary>Removes an owner.</summary>
    [HttpPost("remove-owner"), UiApi("MavenManage", "Index", Handler = "RemoveOwner")]
    public async Task<ActionResult<ScreenResult<MavenManageViewModel>>> RemoveOwner(string? group, string? artifact, [FromBody] MavenManageForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.RemoveOwnerAsync(User, group, artifact, form?.Version, form?.OwnerName, cancellationToken));

    /// <summary>Deletes one file of the chosen version, after the password.</summary>
    [HttpPost("delete-file"), UiApi("MavenManage", "Index", Handler = "DeleteFile")]
    public async Task<ActionResult<ScreenResult<MavenManageViewModel>>> DeleteFile(string? group, string? artifact, [FromBody] MavenManageForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.DeleteFileAsync(User, group, artifact, form?.Version, form?.FileName, form?.Password, cancellationToken));

    /// <summary>Deletes one version, after the password.</summary>
    [HttpPost("delete-version"), UiApi("MavenManage", "Index", Handler = "DeleteVersion")]
    public async Task<ActionResult<ScreenResult<MavenManageViewModel>>> DeleteVersion(string? group, string? artifact, [FromBody] MavenManageForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.DeleteVersionAsync(User, group, artifact, form?.Version, form?.Version, form?.Password, cancellationToken));

    /// <summary>Deletes the whole artifact, after the password.</summary>
    [HttpPost("delete-artifact"), UiApi("MavenManage", "Index", Handler = "DeleteArtifact")]
    public async Task<ActionResult<ScreenResult<MavenManageViewModel>>> DeleteArtifact(string? group, string? artifact, [FromBody] MavenManageForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.DeleteArtifactAsync(User, group, artifact, form?.Version, form?.Password, cancellationToken));

    /// <summary>Deletes old snapshot builds of a version, after the password.</summary>
    [HttpPost("cleanup"), UiApi("MavenManage", "Index", Handler = "Cleanup")]
    public async Task<ActionResult<ScreenResult<MavenManageViewModel>>> Cleanup(string? group, string? artifact, [FromBody] MavenManageForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.CleanupAsync(User, group, artifact, form?.Version, form?.Version, form?.Keep, form?.Password, cancellationToken));
}
