using System;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>The access audit: the Audit page's list.</summary>
[Route(ApiCatalog.RootTemplate + "/audit")]
public class AuditController : AccessApiController
{
    /// <summary>The audit entries, newest first.</summary>
    [HttpGet]
    [ApiOperation("List access-audit entries, newest first, optionally by actor, subject or time", "AdminAudit", "Index", Response = typeof(AccessAuditEntry), PagedList = true)]
    public async Task<IActionResult> List(string? actor, string? subject, DateTime? from, DateTime? to, int? page, int? pageSize)
    {
        if (Unavailable() is { } unavailable) return unavailable;

        var filter = new AuditFilter(
            string.IsNullOrWhiteSpace(actor) ? null : actor.Trim(),
            string.IsNullOrWhiteSpace(subject) ? null : subject.Trim(),
            from?.ToUniversalTime(),
            to?.ToUniversalTime());

        return RespondPage(await AccessAdmin.AuditAsync(filter, 1000, Cancellation), page, pageSize);
    }
}
