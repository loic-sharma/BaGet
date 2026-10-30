using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The retention screens: the policies (<c>/admin/retention</c>), one policy (<c>/admin/retention/{key}</c>) and the legal holds
/// (<c>/admin/retention/holds</c>). They replace the Razor Pages of the same names (same URLs and handlers); the work is
/// done by the JSON twin (<see cref="RetentionApiController"/>) and this controller only renders its answers. Administrators only (404 for
/// anyone else), every POST is refused with 503 while the read-only gate is closed.
/// </summary>
public sealed class AdminRetentionController : RetentionControllerBase
{
    // ---------------------------------------------------------------- policies

    /// <summary>Lists the policies.</summary>
    [Route("admin/retention"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => ShowPolicies(await Api<RetentionApiController, RetentionViewModel>(api => api.GetPolicies(cancellationToken)));

    /// <summary>Creates a policy and opens it.</summary>
    [Route("admin/retention"), HttpPost, PageHandler]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] RetentionPolicyForm? form, string? password, CancellationToken cancellationToken)
        => ShowPolicies(await Api<RetentionApiController, RetentionViewModel>(api => api.Create(new RetentionPolicyBody(form, password), cancellationToken)));

    // ---------------------------------------------------------------- one policy

    /// <summary>Shows the policy.</summary>
    [Route("admin/retention/{key:int}"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Policy(int key, CancellationToken cancellationToken)
        => ShowPolicy(await Api<RetentionApiController, RetentionPolicyViewModel>(api => api.GetPolicy(key, cancellationToken)));

    /// <summary>Saves the rules.</summary>
    [Route("admin/retention/{key:int}"), HttpPost, PageHandler("Save")]
    public async Task<IActionResult> Save(int key, [Bind(Prefix = "Form")] RetentionPolicyForm? form, string? password, CancellationToken cancellationToken)
        => ShowPolicy(await Api<RetentionApiController, RetentionPolicyViewModel>(api => api.Save(key, new RetentionPolicyBody(form, password), cancellationToken)));

    /// <summary>Starts a dry run: the report of what a run would delete.</summary>
    [Route("admin/retention/{key:int}"), HttpPost, PageHandler("DryRun")]
    public async Task<IActionResult> DryRun(int key, CancellationToken cancellationToken)
        => ShowPolicy(await Api<RetentionApiController, RetentionPolicyViewModel>(api => api.DryRun(key, cancellationToken)));

    /// <summary>Starts a run that deletes.</summary>
    [Route("admin/retention/{key:int}"), HttpPost, PageHandler("Run")]
    public async Task<IActionResult> Run(int key, string? password, CancellationToken cancellationToken)
        => ShowPolicy(await Api<RetentionApiController, RetentionPolicyViewModel>(api => api.Run(key, new PasswordForm(password), cancellationToken)));

    /// <summary>Deletes the policy (what it deleted stays deleted).</summary>
    [Route("admin/retention/{key:int}"), HttpPost, PageHandler("Delete")]
    public async Task<IActionResult> Delete(int key, string? password, CancellationToken cancellationToken)
        => ShowPolicy(await Api<RetentionApiController, RetentionPolicyViewModel>(api => api.Delete(key, new PasswordForm(password), cancellationToken)));

    /// <summary>A form posted to the screen's own URL, with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/retention/{key:int}"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> PolicyPost(int key, CancellationToken cancellationToken)
        => ShowPolicy(await Api<RetentionApiController, RetentionPolicyViewModel>(api => api.GetPolicy(key, cancellationToken)));

    // ---------------------------------------------------------------- holds

    /// <summary>Lists the holds.</summary>
    [Route("admin/retention/holds"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Holds([FromQuery] string? repository, CancellationToken cancellationToken)
        => ShowHolds(await Api<RetentionApiController, RetentionHoldsViewModel>(api => api.GetHolds(repository, cancellationToken)));

    /// <summary>Places a hold.</summary>
    [Route("admin/retention/holds"), HttpPost, PageHandler("Add")]
    public async Task<IActionResult> AddHold(
        [FromQuery] string? repository, string? holdRepository, string? holdName, string? holdVersion, string? reason, CancellationToken cancellationToken)
        => ShowHolds(await Api<RetentionApiController, RetentionHoldsViewModel>(
            api => api.AddHold(repository, new RetentionHoldForm(holdRepository, holdName, holdVersion, reason), cancellationToken)));

    /// <summary>Lifts a hold.</summary>
    [Route("admin/retention/holds"), HttpPost, PageHandler("Remove")]
    public async Task<IActionResult> RemoveHold([FromQuery] string? repository, int key, string? password, CancellationToken cancellationToken)
        => ShowHolds(await Api<RetentionApiController, RetentionHoldsViewModel>(api => api.RemoveHold(repository, new RetentionHoldRemoval(key, password), cancellationToken)));

    /// <summary>A form posted to the screen's own URL, with no handler: the screen is rendered (the page had no default handler).</summary>
    [Route("admin/retention/holds"), HttpPost, PageHandler, ImplicitPageHandler]
    public async Task<IActionResult> HoldsPost([FromQuery] string? repository, CancellationToken cancellationToken)
        => ShowHolds(await Api<RetentionApiController, RetentionHoldsViewModel>(api => api.GetHolds(repository, cancellationToken)));

    // ---------------------------------------------------------------- helpers

    private IActionResult ShowPolicies(UiApiResponse<RetentionViewModel> response) => RenderFlashed(response, "Index");

    private IActionResult ShowPolicy(UiApiResponse<RetentionPolicyViewModel> response) => RenderFlashed(response, "Policy");

    private IActionResult ShowHolds(UiApiResponse<RetentionHoldsViewModel> response) => RenderFlashed(response, "Holds");
}
