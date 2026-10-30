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
/// The JSON twin of the npm package management screen: <c>GET /api/ui/npm-manage/{*id}</c> and, for each handler of the page, <c>POST
/// /api/ui/npm-manage/{handler-in-kebab-case}/{*id}</c> with a JSON body (the package name is a catch-all, so the handler comes first; also
/// under <c>/api/ui/r/{repo}/…</c>). Interactive session only (403 over HTTP to an API key and to an anonymous caller; the web screen calls it in process). <c>NpmManageController</c> calls it in process and renders the view.
/// </summary>
[Route("api/ui/npm-manage")]
[RepositoryPage(Format = RepositoryFormat.Npm, NotOnVirtual = true, NotOnRemote = true)]
public sealed class NpmManageApiController : AccountApiControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    private NpmManageUseCase UseCase => HttpContext.RequestServices.GetRequiredService<NpmManageUseCase>();

    /// <summary>The name / repository-kind / ownership check that opens every handler (see <see cref="PackageManageApiController.EnterAsync"/>).</summary>
    [NonAction]
    public Task<ScreenResult<NpmManageViewModel>?> EnterAsync(string? id, string? handler, bool isWrite, CancellationToken cancellationToken)
        => UseCase.EnterAsync(User, id, handler, isWrite, cancellationToken);

    /// <summary>The screen.</summary>
    [HttpGet("{*id}"), UiApi("NpmManage", "Index")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> Get(string? id, CancellationToken cancellationToken)
        => Respond(await UseCase.GetAsync(User, id, cancellationToken));

    /// <summary>Adds an owner.</summary>
    [HttpPost("add-owner/{*id}"), UiApi("NpmManage", "Index", Handler = "AddOwner")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> AddOwner(string? id, [FromBody] NpmManageOwnerForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.AddOwnerAsync(User, id, form?.OwnerName, cancellationToken));

    /// <summary>Removes an owner.</summary>
    [HttpPost("remove-owner/{*id}"), UiApi("NpmManage", "Index", Handler = "RemoveOwner")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> RemoveOwner(string? id, [FromBody] NpmManageOwnerForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.RemoveOwnerAsync(User, id, form?.OwnerName, cancellationToken));

    /// <summary>Points a dist-tag at a version.</summary>
    [HttpPost("set-tag/{*id}"), UiApi("NpmManage", "Index", Handler = "SetTag")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> SetTag(string? id, [FromBody] NpmManageTagForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.SetTagAsync(User, id, form?.Tag, form?.Version, cancellationToken));

    /// <summary>Removes a dist-tag.</summary>
    [HttpPost("remove-tag/{*id}"), UiApi("NpmManage", "Index", Handler = "RemoveTag")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> RemoveTag(string? id, [FromBody] NpmManageTagForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.RemoveTagAsync(User, id, form?.Tag, cancellationToken));

    /// <summary>Deprecates a version.</summary>
    [HttpPost("deprecate/{*id}"), UiApi("NpmManage", "Index", Handler = "Deprecate")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> Deprecate(string? id, [FromBody] NpmManageDeprecateForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.DeprecateAsync(User, id, form?.Version, form?.Message, cancellationToken));

    /// <summary>Removes a version's deprecation.</summary>
    [HttpPost("undeprecate/{*id}"), UiApi("NpmManage", "Index", Handler = "Undeprecate")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> Undeprecate(string? id, [FromBody] NpmManageDeprecateForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.UndeprecateAsync(User, id, form?.Version, cancellationToken));

    /// <summary>Unpublishes one version, after the password is confirmed.</summary>
    [HttpPost("unpublish/{*id}"), UiApi("NpmManage", "Index", Handler = "Unpublish")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> Unpublish(string? id, [FromBody] NpmManageUnpublishForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.UnpublishAsync(User, id, form?.Version, form?.Password, cancellationToken));

    /// <summary>Unpublishes every version, after the password is confirmed.</summary>
    [HttpPost("unpublish-all/{*id}"), UiApi("NpmManage", "Index", Handler = "UnpublishAll")]
    public async Task<ActionResult<ScreenResult<NpmManageViewModel>>> UnpublishAll(string? id, [FromBody] NpmManageUnpublishForm? form, CancellationToken cancellationToken)
        => Respond(await UseCase.UnpublishAllAsync(User, id, form?.Password, cancellationToken));
}
