using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Web.V3.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.Application.Models.Browse;
using BaGet.Web.Application.Screens;
using BaGet.Web.V3.ViewModels;

namespace BaGet.Web.V3.Controllers.FrontEnd;

/// <summary>
/// Report abuse (<c>/packages/{id}/{version}/report-abuse</c>, and under <c>/r/{repo}/</c>; 404 in a virtual repository). Replaces the
/// <c>/ReportAbuse</c> Razor Page. The form is posted with antiforgery
/// validation (<see cref="UiControllerBase"/>). Renders what <see cref="ReportAbuseApiController"/> answers.
/// </summary>
[RepositoryPage(NotOnVirtual = true)]
public sealed class ReportAbuseController : UiControllerBase
{
    /// <summary>The empty form.</summary>
    [Route("packages/{id}/{version}/report-abuse"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Show(string? id, string? version)
        => Render(await Api<ReportAbuseApiController, ReportAbuseScreen>(api => api.Get(id, version)), "Show", ToViewModel);

    /// <summary>Records the report.</summary>
    [Route("packages/{id}/{version}/report-abuse"), HttpPost, PageHandler]
    public async Task<IActionResult> Submit(string? id, string? version, ReportAbuseViewModel model, CancellationToken cancellationToken)
    {
        // The use case validates the form itself (same rules, same field names), so the errors of model binding are dropped, not shown twice.
        ModelState.Clear();
        var form = new ReportAbuseForm { Reason = model.Reason, Details = model.Details, ReporterEmail = model.ReporterEmail };

        return Render(await Api<ReportAbuseApiController, ReportAbuseScreen>(api => api.Submit(id, version, form, cancellationToken)), "Show", ToViewModel);
    }

    private static object? ToViewModel(ReportAbuseScreen screen)
        => new ReportAbuseViewModel
        {
            Id = screen.Id,
            Version = screen.Version,
            Reason = screen.Form.Reason,
            Details = screen.Form.Details,
            ReporterEmail = screen.Form.ReporterEmail,
            Submitted = screen.Submitted,
            CanReport = screen.CanReport,
        };
}
