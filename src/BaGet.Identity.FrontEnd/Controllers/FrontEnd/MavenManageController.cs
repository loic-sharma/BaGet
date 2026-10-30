using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.Screens;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Models.Packages;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The owners' screen for a Maven artifact (<c>/maven-manage/{group}/{artifact}</c>, also at <c>/r/{repo}/…</c>): owners, versions,
/// the files of the chosen version, deletion and snapshot clean-up. It replaces the <c>/Packages/MavenManage</c> Razor Page (same
/// URLs, same <c>?handler=</c> forms, same gates); the work is done by <see cref="MavenManageApiController"/> (the JSON twin, over
/// <c>MavenManageUseCase</c>), which this controller calls in process and renders. The coordinates / repository-kind / ownership check
/// runs in <see cref="OnActionExecutionAsync"/>, before the global repository filter.
/// </summary>
[Route("maven-manage/{group}/{artifact}")]
[RepositoryPage(Format = RepositoryFormat.Maven, NotOnVirtual = true, NotOnRemote = true)]
public sealed class MavenManageController : AccountControllerBase
{
    private const string ViewName = "Index";

    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    private string? Group => RouteData.Values.TryGetValue("group", out var group) ? group as string : null;

    private string? Artifact => RouteData.Values.TryGetValue("artifact", out var artifact) ? artifact as string : null;

    /// <summary>The version whose files are listed: the form field of a POST, else the query string.</summary>
    private string? SelectedVersion => PackageControllerSupport.FormOrQuery(Request, "version");

    /// <inheritdoc/>
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        OnActionExecuting(context);
        if (context.Result != null) return;

        var refusal = await UiApiEntry.InvokeAsync<MavenManageApiController, MavenManageViewModel>(
            this,
            api => api.EnterAsync(
                PackageControllerSupport.Route(context, "group"),
                PackageControllerSupport.Route(context, "artifact"),
                PackageControllerSupport.FormOrQuery(context, "version"),
                PackageControllerSupport.HandlerName(context),
                HttpMethods.IsPost(Request.Method),
                context.HttpContext.RequestAborted));
        if (refusal != null)
        {
            context.Result = Present(refusal);
            return;
        }

        OnActionExecuted(await next());
    }

    /// <summary>The screen.</summary>
    [HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => Present(await Api<MavenManageApiController, MavenManageViewModel>(api => api.Get(Group, Artifact, SelectedVersion, cancellationToken)));

    /// <summary>Adds an owner.</summary>
    [HttpPost, PageHandler("AddOwner")]
    public async Task<IActionResult> AddOwner(string ownerName, CancellationToken cancellationToken)
        => Present(await Api<MavenManageApiController, MavenManageViewModel>(api => api.AddOwner(Group, Artifact, new MavenManageForm { Version = SelectedVersion, OwnerName = ownerName }, cancellationToken)));

    /// <summary>Removes an owner.</summary>
    [HttpPost, PageHandler("RemoveOwner")]
    public async Task<IActionResult> RemoveOwner(string ownerName, CancellationToken cancellationToken)
        => Present(await Api<MavenManageApiController, MavenManageViewModel>(api => api.RemoveOwner(Group, Artifact, new MavenManageForm { Version = SelectedVersion, OwnerName = ownerName }, cancellationToken)));

    /// <summary>Deletes one file of the chosen version, after the password.</summary>
    [HttpPost, PageHandler("DeleteFile")]
    public async Task<IActionResult> DeleteFile(string? fileName, string? password, CancellationToken cancellationToken)
        => Present(await Api<MavenManageApiController, MavenManageViewModel>(api => api.DeleteFile(Group, Artifact, new MavenManageForm { Version = SelectedVersion, FileName = fileName, Password = password }, cancellationToken)));

    /// <summary>Deletes one version, after the password.</summary>
    [HttpPost, PageHandler("DeleteVersion")]
    public async Task<IActionResult> DeleteVersion(string? version, string? password, CancellationToken cancellationToken)
        => Present(await Api<MavenManageApiController, MavenManageViewModel>(api => api.DeleteVersion(Group, Artifact, new MavenManageForm { Version = version, Password = password }, cancellationToken)));

    /// <summary>Deletes the whole artifact, after the password.</summary>
    [HttpPost, PageHandler("DeleteArtifact")]
    public async Task<IActionResult> DeleteArtifact(string? password, CancellationToken cancellationToken)
        => Present(await Api<MavenManageApiController, MavenManageViewModel>(api => api.DeleteArtifact(Group, Artifact, new MavenManageForm { Version = SelectedVersion, Password = password }, cancellationToken)));

    /// <summary>Deletes old snapshot builds of a version, after the password.</summary>
    [HttpPost, PageHandler("Cleanup")]
    public async Task<IActionResult> Cleanup(string? version, string? keep, string? password, CancellationToken cancellationToken)
        => Present(await Api<MavenManageApiController, MavenManageViewModel>(api => api.Cleanup(Group, Artifact, new MavenManageForm { Version = version, Keep = keep, Password = password }, cancellationToken)));

    /// <summary>
    /// A POST that names no handler (or one this screen does not have), such as a stray <c>&lt;form&gt;</c> without
    /// <c>?handler=</c>: the antiforgery check has already run, and there is nothing to do, so the answer is 404. (A Razor Page
    /// with only named POST handlers behaved alike for the caller; without this action routing would answer 404 before the
    /// antiforgery filter and an unprotected POST would not be refused with 400.) It is marked
    /// <see cref="FallbackHandlerAttribute"/>: it is not one of the screen's handlers, so it stays out of the route contract.
    /// </summary>
    [HttpPost, PageHandler, FallbackHandler]
    public IActionResult NoHandler() => NotFound();

    private IActionResult Present(UiApiResponse<MavenManageViewModel> response) => Render(response, ViewName);
}
