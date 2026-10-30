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
/// The owners' screen for an npm package (<c>/npm-manage/{*id}</c>, also at <c>/r/{repo}/…</c>): owners, dist-tags, deprecation and
/// unpublishing. It replaces the <c>/Packages/NpmManage</c> Razor Page (same URLs, same <c>?handler=</c> forms, same gates); the work is done
/// by <see cref="NpmManageApiController"/> (the JSON twin, over <c>NpmManageUseCase</c>), which this controller calls in process and
/// renders. The name / repository-kind / ownership check runs in <see cref="OnActionExecutionAsync"/>, before the global repository filter.
/// </summary>
[Route("npm-manage/{*id}")]
[RepositoryPage(Format = RepositoryFormat.Npm, NotOnVirtual = true, NotOnRemote = true)]
public sealed class NpmManageController : AccountControllerBase
{
    private const string ViewName = "Index";

    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    private string? Id => RouteData.Values.TryGetValue("id", out var id) ? id as string : null;

    /// <inheritdoc/>
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        OnActionExecuting(context);
        if (context.Result != null) return;

        var refusal = await UiApiEntry.InvokeAsync<NpmManageApiController, NpmManageViewModel>(
            this,
            api => api.EnterAsync(PackageControllerSupport.Route(context, "id"), PackageControllerSupport.HandlerName(context), HttpMethods.IsPost(Request.Method), context.HttpContext.RequestAborted));
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
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.Get(Id, cancellationToken)));

    /// <summary>Adds an owner.</summary>
    [HttpPost, PageHandler("AddOwner")]
    public async Task<IActionResult> AddOwner(string ownerName, CancellationToken cancellationToken)
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.AddOwner(Id, new NpmManageOwnerForm { OwnerName = ownerName }, cancellationToken)));

    /// <summary>Removes an owner.</summary>
    [HttpPost, PageHandler("RemoveOwner")]
    public async Task<IActionResult> RemoveOwner(string ownerName, CancellationToken cancellationToken)
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.RemoveOwner(Id, new NpmManageOwnerForm { OwnerName = ownerName }, cancellationToken)));

    /// <summary>Points a dist-tag at a version.</summary>
    [HttpPost, PageHandler("SetTag")]
    public async Task<IActionResult> SetTag(string tag, string version, CancellationToken cancellationToken)
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.SetTag(Id, new NpmManageTagForm { Tag = tag, Version = version }, cancellationToken)));

    /// <summary>Removes a dist-tag.</summary>
    [HttpPost, PageHandler("RemoveTag")]
    public async Task<IActionResult> RemoveTag(string tag, CancellationToken cancellationToken)
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.RemoveTag(Id, new NpmManageTagForm { Tag = tag }, cancellationToken)));

    /// <summary>Deprecates a version.</summary>
    [HttpPost, PageHandler("Deprecate")]
    public async Task<IActionResult> Deprecate(string version, string? message, CancellationToken cancellationToken)
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.Deprecate(Id, new NpmManageDeprecateForm { Version = version, Message = message }, cancellationToken)));

    /// <summary>Removes a version's deprecation.</summary>
    [HttpPost, PageHandler("Undeprecate")]
    public async Task<IActionResult> Undeprecate(string version, CancellationToken cancellationToken)
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.Undeprecate(Id, new NpmManageDeprecateForm { Version = version }, cancellationToken)));

    /// <summary>Unpublishes one version, after the password is confirmed.</summary>
    [HttpPost, PageHandler("Unpublish")]
    public async Task<IActionResult> Unpublish(string version, string? password, CancellationToken cancellationToken)
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.Unpublish(Id, new NpmManageUnpublishForm { Version = version, Password = password }, cancellationToken)));

    /// <summary>Unpublishes every version, after the password is confirmed.</summary>
    [HttpPost, PageHandler("UnpublishAll")]
    public async Task<IActionResult> UnpublishAll(string? password, CancellationToken cancellationToken)
        => Present(await Api<NpmManageApiController, NpmManageViewModel>(api => api.UnpublishAll(Id, new NpmManageUnpublishForm { Password = password }, cancellationToken)));

    /// <summary>
    /// A POST that names no handler (or one this screen does not have), such as a stray <c>&lt;form&gt;</c> without
    /// <c>?handler=</c>: the antiforgery check has already run, and there is nothing to do, so the answer is 404. (A Razor Page
    /// with only named POST handlers behaved alike for the caller; without this action routing would answer 404 before the
    /// antiforgery filter and an unprotected POST would not be refused with 400.) It is marked
    /// <see cref="FallbackHandlerAttribute"/>: it is not one of the screen's handlers, so it stays out of the route contract.
    /// </summary>
    [HttpPost, PageHandler, FallbackHandler]
    public IActionResult NoHandler() => NotFound();

    private IActionResult Present(UiApiResponse<NpmManageViewModel> response) => Render(response, ViewName);
}
