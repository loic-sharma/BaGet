using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.Screens;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.UseCases.Packages;
using BaGet.Identity.Models.Packages;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The owners' screen for a package ID (<c>/packages/{id}/manage</c>, also at <c>/r/{repo}/…</c>): owners, versions, deprecation,
/// vulnerabilities, symbols and abuse reports. It replaces the <c>/Packages/Manage</c> Razor Page (same URLs, same
/// <c>?handler=</c> forms, same gates); the work is done by <see cref="PackageManageApiController"/> (the JSON twin, over
/// <see cref="PackageManageUseCase"/>), which this controller calls in process and renders.
/// </summary>
/// <remarks>
/// The fallback <c>NoHandler</c> is marked <see cref="FallbackHandlerAttribute"/>: it is not a handler of the screen.
/// The ownership / administrator check runs in <see cref="OnActionExecutionAsync"/>, before the global repository filter.
/// </remarks>
[Route("packages/{id}/manage")]
[RepositoryPage(NotOnVirtual = true)]
public sealed class PackageManageController : AccountControllerBase
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

        var refusal = await UiApiEntry.InvokeAsync<PackageManageApiController, PackageManageScreen>(
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
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.Get(Id!, cancellationToken)));

    /// <summary>Adds an owner.</summary>
    [HttpPost, PageHandler("AddOwner")]
    public async Task<IActionResult> AddOwner(string ownerName, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.AddOwner(Id!, new PackageManageOwnerForm { OwnerName = ownerName }, cancellationToken)));

    /// <summary>Removes an owner.</summary>
    [HttpPost, PageHandler("RemoveOwner")]
    public async Task<IActionResult> RemoveOwner(string ownerName, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.RemoveOwner(Id!, new PackageManageOwnerForm { OwnerName = ownerName }, cancellationToken)));

    /// <summary>Unlists a version.</summary>
    [HttpPost, PageHandler("Unlist")]
    public async Task<IActionResult> Unlist(string version, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.Unlist(Id!, new PackageManageVersionForm { Version = version }, cancellationToken)));

    /// <summary>Relists a version.</summary>
    [HttpPost, PageHandler("Relist")]
    public async Task<IActionResult> Relist(string version, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.Relist(Id!, new PackageManageVersionForm { Version = version }, cancellationToken)));

    /// <summary>Deletes a version as the server is configured to (unlist or permanent).</summary>
    [HttpPost, PageHandler("Delete")]
    public async Task<IActionResult> Delete(string version, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.Delete(Id!, new PackageManageVersionForm { Version = version }, cancellationToken)));

    /// <summary>Permanently deletes a version (administrators, after the password).</summary>
    [HttpPost, PageHandler("HardDelete")]
    public async Task<IActionResult> HardDelete(string version, string? password, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.HardDelete(Id!, new PackageManageHardDeleteForm { Version = version, Password = password }, cancellationToken)));

    /// <summary>Permanently deletes every version and the owners (administrators, after the password).</summary>
    [HttpPost, PageHandler("HardDeleteAll")]
    public async Task<IActionResult> HardDeleteAll(string? password, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.HardDeleteAll(Id!, new PackageManagePasswordForm { Password = password }, cancellationToken)));

    /// <summary>Deprecates a version.</summary>
    [HttpPost, PageHandler("Deprecate")]
    public async Task<IActionResult> Deprecate(
        string version,
        List<string>? reasons,
        string message,
        string alternatePackageId,
        string? alternateVersionRange,
        CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.Deprecate(
            Id!,
            new PackageManageDeprecateForm { Version = version, Reasons = reasons, Message = message, AlternatePackageId = alternatePackageId, AlternateVersionRange = alternateVersionRange },
            cancellationToken)));

    /// <summary>Removes a version's deprecation.</summary>
    [HttpPost, PageHandler("Undeprecate")]
    public async Task<IActionResult> Undeprecate(string version, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.Undeprecate(Id!, new PackageManageVersionForm { Version = version }, cancellationToken)));

    /// <summary>Adds a vulnerability to a version.</summary>
    [HttpPost, PageHandler("AddVulnerability")]
    public async Task<IActionResult> AddVulnerability(
        string version,
        string? advisoryUrl,
        int severity,
        string? versionRange,
        CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.AddVulnerability(
            Id!,
            new PackageManageVulnerabilityForm { Version = version, AdvisoryUrl = advisoryUrl, Severity = severity, VersionRange = versionRange },
            cancellationToken)));

    /// <summary>Removes a vulnerability from a version.</summary>
    [HttpPost, PageHandler("RemoveVulnerability")]
    public async Task<IActionResult> RemoveVulnerability(string version, int key, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.RemoveVulnerability(Id!, new PackageManageRemoveVulnerabilityForm { Version = version, Key = key }, cancellationToken)));

    /// <summary>Marks or unmarks the ID as verified (administrators).</summary>
    [HttpPost, PageHandler("SetVerified")]
    public async Task<IActionResult> SetVerified(bool verified, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.SetVerified(Id!, new PackageManageVerifiedForm { Verified = verified }, cancellationToken)));

    /// <summary>Deletes the PDBs recorded for a version.</summary>
    [HttpPost, PageHandler("DeleteSymbols")]
    public async Task<IActionResult> DeleteSymbols(string version, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.DeleteSymbols(Id!, new PackageManageVersionForm { Version = version }, cancellationToken)));

    /// <summary>Resolves an abuse report.</summary>
    [HttpPost, PageHandler("ResolveReport")]
    public async Task<IActionResult> ResolveReport(int key, CancellationToken cancellationToken)
        => Present(await Api<PackageManageApiController, PackageManageScreen>(api => api.ResolveReport(Id!, new PackageManageReportForm { Key = key }, cancellationToken)));

    /// <summary>
    /// A POST that names no handler (or one this screen does not have), such as a stray <c>&lt;form&gt;</c> without
    /// <c>?handler=</c>: the antiforgery check has already run, and there is nothing to do, so the answer is 404. (A Razor Page
    /// with only named POST handlers behaved alike for the caller; without this action routing would answer 404 before the
    /// antiforgery filter and an unprotected POST would not be refused with 400.) It is marked
    /// <see cref="FallbackHandlerAttribute"/>: it is not one of the screen's handlers, so it stays out of the route contract.
    /// </summary>
    [HttpPost, PageHandler, FallbackHandler]
    public IActionResult NoHandler() => NotFound();

    private IActionResult Present(UiApiResponse<PackageManageScreen> response)
        => Render(response, ViewName, screen => ManageViewModel.From(screen));
}
