using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using BaGet.Identity.Application.Models.Access;
using BaGet.Identity.Application.UseCases.Access;

namespace BaGet.Identity.Controllers.UI;

/// <summary>
/// The JSON twin of the access audit log (<c>/api/ui/admin/audit</c>): one entry per change to groups, memberships, permission targets, grants,
/// projects, the repositories' project and anonymous-read switch, newest first, filtered by <c>actor</c>, <c>subject</c>, <c>from</c> and <c>to</c>.
/// Read only; server administrators only. <c>AdminAuditController</c> calls it in process.
/// </summary>
[Route("api/ui/admin/audit")]
public sealed class AdminAuditApiController : AccessApiControllerBase
{
    /// <summary>Lists the entries that match the filter.</summary>
    [HttpGet(""), UiApi("AdminAudit", "Index")]
    public async Task<ActionResult<ScreenResult<AuditViewModel>>> Get(string? actor, string? subject, string? from, string? to, CancellationToken cancellationToken = default)
        => Respond(await UseCase<AdminAuditUseCase>().GetAsync(new AuditQuery(actor, subject, from, to), cancellationToken));
}
