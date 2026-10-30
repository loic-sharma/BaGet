using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Operations;
using BaGet.Identity.Application.Models.Repositories;
using BaGet.Identity.Application.Models.Settings;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The administrator's operational screens: maintenance operations (<c>/admin/operations</c>), the validation queue
/// (<c>/admin/validation</c>), abuse reports (<c>/admin/abuse</c>), the cluster status (<c>/admin/cluster</c>), the package formats
/// (<c>/admin/formats</c>) and the notice shown where there is no access model (<c>/admin/access-unavailable</c>). They replace the
/// Razor Pages of the same names (same URLs and handlers); the work is done by the JSON twin
/// (<see cref="AdminOperationsApiController"/>) and this controller only renders its answers. Every write is refused with 503 while
/// the read-only gate is closed.
/// </summary>
public sealed class AdminOperationsController : AdminControllerBase
{
    // ---------------------------------------------------------------- Operations

    /// <summary>Lists the operations and their status.</summary>
    [Route("admin/operations"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, OperationsViewModel>(api => api.GetOperations(cancellationToken)), "Index");

    /// <summary>Starts an operation on a repository.</summary>
    [Route("admin/operations"), HttpPost, PageHandler]
    public async Task<IActionResult> Start(string? operation, string? repository, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, OperationsViewModel>(api => api.Start(new OperationForm(operation, repository, password), cancellationToken)), "Index");

    // ---------------------------------------------------------------- Validation

    /// <summary>Lists the pending and quarantined submissions.</summary>
    [Route("admin/validation"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Validation(CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, ValidationViewModel>(api => api.GetValidation(cancellationToken)), "Validation");

    /// <summary>Validates the submission again.</summary>
    [Route("admin/validation"), HttpPost, PageHandler("Retry")]
    public async Task<IActionResult> Retry(string? submissionId, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, ValidationViewModel>(api => api.Retry(new SubmissionForm(submissionId, null), cancellationToken)), "Validation");

    /// <summary>Indexes the quarantined package although validation failed.</summary>
    [Route("admin/validation"), HttpPost, PageHandler("Release")]
    public async Task<IActionResult> Release(string? submissionId, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, ValidationViewModel>(api => api.Release(new SubmissionForm(submissionId, password), cancellationToken)), "Validation");

    /// <summary>Deletes the quarantined package.</summary>
    [Route("admin/validation"), HttpPost, PageHandler("Reject")]
    public async Task<IActionResult> Reject(string? submissionId, string? password, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, ValidationViewModel>(api => api.Reject(new SubmissionForm(submissionId, password), cancellationToken)), "Validation");

    // ---------------------------------------------------------------- Abuse

    /// <summary>Lists the abuse reports (the unresolved ones unless <c>all</c>).</summary>
    [Route("admin/abuse"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Abuse(bool all, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, AbuseViewModel>(api => api.GetAbuse(all, cancellationToken)), "Abuse");

    /// <summary>Marks a report resolved.</summary>
    [Route("admin/abuse"), HttpPost, PageHandler("Resolve")]
    public async Task<IActionResult> Resolve(int key, bool all, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, AbuseViewModel>(api => api.Resolve(new AbuseForm(key, all), cancellationToken)), "Abuse");

    /// <summary>Reopens a resolved report.</summary>
    [Route("admin/abuse"), HttpPost, PageHandler("Reopen")]
    public async Task<IActionResult> Reopen(int key, bool all, CancellationToken cancellationToken)
        => RenderFlashed(await Api<AdminOperationsApiController, AbuseViewModel>(api => api.Reopen(new AbuseForm(key, all), cancellationToken)), "Abuse");

    // ---------------------------------------------------------------- Cluster, Formats, AccessUnavailable

    /// <summary>The cluster status; <c>?refresh=true</c> runs the health checks again.</summary>
    [Route("admin/cluster"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Cluster(bool refresh, CancellationToken cancellationToken)
        => Render(await Api<AdminOperationsApiController, ClusterViewModel>(api => api.GetCluster(refresh, cancellationToken)), "Cluster");

    /// <summary>The package formats this server can host.</summary>
    [Route("admin/formats"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Formats(CancellationToken cancellationToken)
        => Render(await Api<AdminOperationsApiController, FormatsViewModel>(api => api.GetFormats(cancellationToken)), "Formats");

    /// <summary>
    /// Where the access screens send an administrator when the configured database has no access model (Azure Table): says so and
    /// explains what applies instead. The page had no handler method at all, hence no <see cref="PageHandlerAttribute"/>. The page
    /// has fixed text, so the view takes no model.
    /// </summary>
    [Route("admin/access-unavailable"), HttpGet, HttpHead]
    public async Task<IActionResult> AccessUnavailable()
        => Render(await Api<AdminOperationsApiController, AccessUnavailableViewModel>(api => Task.FromResult(api.GetAccessUnavailable())), "AccessUnavailable", _ => null);
}
