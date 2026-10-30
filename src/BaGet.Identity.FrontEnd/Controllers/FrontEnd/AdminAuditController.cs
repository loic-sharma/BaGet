using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Identity.Controllers.UI;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Access;

namespace BaGet.Identity.Controllers.FrontEnd;

/// <summary>
/// The access audit log (<c>/admin/audit</c>): one entry per change to groups, memberships, permission targets, grants, projects, the
/// repositories' project and anonymous-read switch, newest first, filtered by who made the change, what it was done to and the dates.
/// Read only; server administrators only. It replaces the Razor Page <c>/Admin/Audit</c> (same URL); the work is done by
/// the JSON twin (<see cref="AdminAuditApiController"/>) and this controller only renders its answer.
/// </summary>
public sealed class AdminAuditController : AccessControllerBase
{
    /// <summary>Lists the entries that match the filter (<c>actor</c>, <c>subject</c>, <c>from</c>, <c>to</c>).</summary>
    [Route("admin/audit"), HttpGet, HttpHead, PageHandler]
    public async Task<IActionResult> Index(string? actor, string? subject, string? from, string? to, CancellationToken cancellationToken)
        => Render(await Api<AdminAuditApiController, AuditViewModel>(api => api.Get(actor, subject, from, to, cancellationToken)), "Index");
}
