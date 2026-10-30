using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Operations;
using BaGet.Identity.Application.Models.Repositories;
using BaGet.Identity.Application.Models.Settings;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.UseCases.Admin;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the administrator's operational screens (<c>/api/ui/admin/...</c>): maintenance operations, the validation queue, abuse reports, the
/// package formats, the cluster status and the "access control unavailable" notice. <c>AdminOperationsController</c> calls it in process. Every write is
/// refused with 503 while the server is read-only; releasing or rejecting a submission and starting an operation that deletes data ask the administrator's
/// password in the body.
/// </summary>
[Route("api/ui/admin")]
public sealed class AdminOperationsApiController : AdminApiControllerBase
{
    private AdminOperationsUseCase Operations => UseCase<AdminOperationsUseCase>();

    // ---------------------------------------------------------------- Operations

    /// <summary>Lists the operations and their status.</summary>
    [HttpGet("operations"), UiApi("AdminOperations", "Index")]
    public async Task<ActionResult<ScreenResult<OperationsViewModel>>> GetOperations(CancellationToken cancellationToken = default)
        => Respond(await Operations.GetOperationsAsync(User, "~" + Request.Path + Request.QueryString, cancellationToken));

    /// <summary>Starts an operation on a repository.</summary>
    [HttpPost("operations"), UiApi("AdminOperations", "Index")]
    public async Task<ActionResult<ScreenResult<OperationsViewModel>>> Start([FromBody] OperationForm? form, CancellationToken cancellationToken = default)
        => Respond(await Operations.StartAsync(User, form?.Operation, form?.Repository, form?.Password, cancellationToken));

    // ---------------------------------------------------------------- Validation

    /// <summary>Lists the pending and quarantined submissions.</summary>
    [HttpGet("validation"), UiApi("AdminOperations", "Validation")]
    public async Task<ActionResult<ScreenResult<ValidationViewModel>>> GetValidation(CancellationToken cancellationToken = default)
        => Respond(await Operations.GetValidationAsync(User, cancellationToken));

    /// <summary>Validates the submission again.</summary>
    [HttpPost("validation/retry"), UiApi("AdminOperations", "Validation", Handler = "Retry")]
    public async Task<ActionResult<ScreenResult<ValidationViewModel>>> Retry([FromBody] SubmissionForm? form, CancellationToken cancellationToken = default)
        => Respond(await Operations.RetryAsync(User, form?.SubmissionId, cancellationToken));

    /// <summary>Indexes the quarantined package although validation failed.</summary>
    [HttpPost("validation/release"), UiApi("AdminOperations", "Validation", Handler = "Release")]
    public async Task<ActionResult<ScreenResult<ValidationViewModel>>> Release([FromBody] SubmissionForm? form, CancellationToken cancellationToken = default)
        => Respond(await Operations.ReleaseAsync(User, form?.SubmissionId, form?.Password, cancellationToken));

    /// <summary>Deletes the quarantined package.</summary>
    [HttpPost("validation/reject"), UiApi("AdminOperations", "Validation", Handler = "Reject")]
    public async Task<ActionResult<ScreenResult<ValidationViewModel>>> Reject([FromBody] SubmissionForm? form, CancellationToken cancellationToken = default)
        => Respond(await Operations.RejectAsync(User, form?.SubmissionId, form?.Password, cancellationToken));

    // ---------------------------------------------------------------- Abuse

    /// <summary>Lists the abuse reports (the unresolved ones unless <paramref name="all"/>).</summary>
    [HttpGet("abuse"), UiApi("AdminOperations", "Abuse")]
    public async Task<ActionResult<ScreenResult<AbuseViewModel>>> GetAbuse(bool all, CancellationToken cancellationToken = default)
        => Respond(await Operations.GetAbuseAsync(all, cancellationToken));

    /// <summary>Marks a report resolved.</summary>
    [HttpPost("abuse/resolve"), UiApi("AdminOperations", "Abuse", Handler = "Resolve")]
    public async Task<ActionResult<ScreenResult<AbuseViewModel>>> Resolve([FromBody] AbuseForm? form, CancellationToken cancellationToken = default)
        => Respond(await Operations.ResolveAsync(User, form?.Key ?? 0, form?.All ?? false, cancellationToken));

    /// <summary>Reopens a resolved report.</summary>
    [HttpPost("abuse/reopen"), UiApi("AdminOperations", "Abuse", Handler = "Reopen")]
    public async Task<ActionResult<ScreenResult<AbuseViewModel>>> Reopen([FromBody] AbuseForm? form, CancellationToken cancellationToken = default)
        => Respond(await Operations.ReopenAsync(User, form?.Key ?? 0, form?.All ?? false, cancellationToken));

    // ---------------------------------------------------------------- Cluster, Formats, AccessUnavailable

    /// <summary>The cluster status; <c>?refresh=true</c> runs the health checks again.</summary>
    [HttpGet("cluster"), UiApi("AdminOperations", "Cluster")]
    public async Task<ActionResult<ScreenResult<ClusterViewModel>>> GetCluster(bool refresh, CancellationToken cancellationToken = default)
        => Respond(await Operations.GetClusterAsync(refresh, cancellationToken));

    /// <summary>The package formats this server can host.</summary>
    [HttpGet("formats"), UiApi("AdminOperations", "Formats")]
    public async Task<ActionResult<ScreenResult<FormatsViewModel>>> GetFormats(CancellationToken cancellationToken = default)
        => Respond(await Operations.GetFormatsAsync(cancellationToken));

    /// <summary>Why the access screens are not available: the configured database has no access model.</summary>
    [HttpGet("access-unavailable"), UiApi("AdminOperations", "AccessUnavailable")]
    public ActionResult<ScreenResult<AccessUnavailableViewModel>> GetAccessUnavailable()
        => Respond(Operations.GetAccessUnavailable());
}
