using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.EntityFrameworkCore;
using BaGet.Identity.Application.Models.Operations;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// Where a pushed package stands in validation: for each version of the ID, its newest submission and the verdict and message of every step.
/// The screen counterpart of <c>GET v3/package/{id}/{version}/validation</c>, gated the same way: a signed-in user who may push or manage the
/// ID (an administrator holds both). Nothing is written.
/// </summary>
public sealed class ValidationStatusUseCase
{
    private readonly PackageScreenServices _services;
    private readonly IPackageAuthorizationService _authorization;
    private readonly IContext _context;

    /// <summary>Creates the use case.</summary>
    public ValidationStatusUseCase(PackageScreenServices services, IPackageAuthorizationService authorization, IContext context)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Shows the submissions of the ID, forbidden without the permission, 404 when validation never saw it.</summary>
    public async Task<ScreenResult<ValidationStatusViewModel>> GetAsync(ClaimsPrincipal caller, string? id, CancellationToken cancellationToken)
    {
        var principal = await _services.CallerAsync(caller).ConfigureAwait(false);
        if (principal == null) return ScreenResult<ValidationStatusViewModel>.SignIn();

        if (!await _authorization.AuthorizeAsync(principal, PackageAction.Push, id, cancellationToken).ConfigureAwait(false)
            && !await _authorization.AuthorizeAsync(principal, PackageAction.Manage, id, cancellationToken).ConfigureAwait(false))
        {
            return ScreenResult<ValidationStatusViewModel>.Forbidden();
        }

        if (_context is NullContext) return ScreenResult<ValidationStatusViewModel>.NotFound();

        var lowerId = (id ?? string.Empty).ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        var steps = await _context.PackageValidations
            .AsNoTracking()
            .Where(v => v.PackageId.ToLower() == lowerId)
            .OrderByDescending(v => v.Key)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        #pragma warning restore CS8602, CS8604
        if (steps.Count == 0) return ScreenResult<ValidationStatusViewModel>.NotFound();

        // The newest submission of a version is the one that matters, as in the API.
        return ScreenResult<ValidationStatusViewModel>.Ok(new ValidationStatusViewModel
        {
            Id = id,
            Submissions = steps
                .GroupBy(s => s.PackageVersion, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var latest = g.First().SubmissionId;
                    var submission = g.Where(s => s.SubmissionId == latest).OrderBy(s => s.Key).ToList();
                    var index = submission.FirstOrDefault(s => s.Type == PackageValidation.IndexType);

                    return new ValidationSubmission(
                        g.Key ?? string.Empty,
                        latest,
                        index?.State switch
                        {
                            PackageValidationState.Succeeded => "Available",
                            PackageValidationState.Failed => "Quarantined",
                            _ => "Validating",
                        },
                        submission);
                })
                .OrderByDescending(s => NuGet.Versioning.NuGetVersion.TryParse(s.Version, out var v) ? v : null)
                .ToList(),
        });
    }
}
