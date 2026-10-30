using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using BaGet.Validation.Models;

namespace BaGet.Validation.Controllers.Protocols;

/// <summary>
/// A package version's validation, for its owners: whether it is pending, available
/// or quarantined, and why.
/// </summary>
public class ValidationStatusController : Controller
{
    private readonly IContext _context;
    private readonly Core.IAuthenticationService _authentication;
    private readonly IPackageAuthorizationService _authorization;

    public ValidationStatusController(
        IContext context,
        Core.IAuthenticationService authentication,
        IPackageAuthorizationService authorization)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    [HttpGet]
    public async Task<IActionResult> Get(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion)) return NotFound();

        var principal = await _authentication.AuthenticatePrincipalAsync(Request.Headers["X-NuGet-ApiKey"], cancellationToken);
        if (principal == null) return Unauthorized();
        if (!await _authorization.AuthorizeAsync(principal, PackageAction.Push, id, cancellationToken)
            && !await _authorization.AuthorizeAsync(principal, PackageAction.Manage, id, cancellationToken))
        {
            return StatusCode(403);
        }

        if (_context is NullContext) return NotFound();

        var lowerId = id.ToLowerInvariant();
        var normalized = nugetVersion.ToNormalizedString();
        #pragma warning disable CS8602, CS8604
        var steps = await _context.PackageValidations
            .AsNoTracking()
            .Where(v => v.PackageId.ToLower() == lowerId && v.PackageVersion == normalized)
            .OrderByDescending(v => v.Key)
            .ToListAsync(cancellationToken);
        #pragma warning restore CS8602, CS8604

        if (steps.Count == 0) return NotFound();

        // The newest submission is the one that matters.
        var latest = steps[0].SubmissionId;
        var submission = steps.Where(s => s.SubmissionId == latest).OrderBy(s => s.Key).ToList();
        var index = submission.Single(s => s.Type == PackageValidation.IndexType);

        return Json(new ValidationStatusResponse
        {
            SubmissionId = latest,
            Status = index.State switch
            {
                PackageValidationState.Succeeded => "Available",
                PackageValidationState.Failed => "Quarantined",
                _ => "Validating",
            },
            Steps = submission
                .Select(s => new ValidationStep { Type = s.Type, State = s.State.ToString(), Message = s.Message })
                .ToList(),
        });
    }
}
