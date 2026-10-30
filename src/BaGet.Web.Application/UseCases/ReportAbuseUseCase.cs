using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using BaGet.Web.Application.Models.Browse;
using BaGet.Web.Application.Screens;

namespace BaGet.Web.Application.UseCases;

/// <summary>
/// The report-abuse screen (<c>/packages/{id}/{version}/report-abuse</c>, and under <c>/r/{repo}/</c>; 404 in a virtual repository):
/// records a report against a package version, for its owners and the administrator. Works for any ID and version (a client expands the
/// report abuse template from whatever it is looking at).
/// </summary>
public sealed class ReportAbuseUseCase
{
    private readonly IContext _context;
    private readonly SystemTime _time;

    /// <summary>Creates the use case.</summary>
    public ReportAbuseUseCase(IContext context, SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Reports are kept in the database, which Azure Table storage cannot do.</summary>
    public bool CanReport => !(_context is NullContext);

    /// <summary>The empty form for a package version.</summary>
    public ScreenResult<ReportAbuseScreen> Get(string? id, string? version)
        => ScreenResult<ReportAbuseScreen>.Ok(new ReportAbuseScreen(id, version, CanReport, false, new ReportAbuseForm()));

    /// <summary>
    /// Validates and records the report: Ok with <c>Submitted</c> once recorded, Invalid with the reasons (keyed by field name) to show the
    /// form again, NotFound when reports are unsupported or the ID or version is not one.
    /// </summary>
    public async Task<ScreenResult<ReportAbuseScreen>> SubmitAsync(string? id, string? version, ReportAbuseForm form, CancellationToken cancellationToken)
    {
        if (form == null) throw new ArgumentNullException(nameof(form));

        var canReport = CanReport;
        if (!canReport) return ScreenResult<ReportAbuseScreen>.NotFound();

        var errors = Validate(form);
        if (!AbuseReport.Reasons.Contains(form.Reason, StringComparer.Ordinal))
        {
            Add(errors, nameof(ReportAbuseForm.Reason), "Choose one of the reasons listed.");
        }

        if (id == null || id.Length > AbstractContext<DbContext>.MaxPackageIdLength || !NuGetVersion.TryParse(version, out var parsed))
        {
            return ScreenResult<ReportAbuseScreen>.NotFound();
        }

        var screen = new ReportAbuseScreen(id, version, canReport, false, form);
        if (errors.Count > 0)
        {
            return ScreenResult<ReportAbuseScreen>.Invalid(screen, errors.ToDictionary(p => p.Key, p => p.Value.ToArray()));
        }

        #pragma warning disable CS8602
        _context.AbuseReports.Add(new AbuseReport
        {
            PackageId = id,
            PackageVersion = parsed.ToNormalizedString(),
            Reason = form.Reason,
            Details = form.Details,
            ReporterEmail = string.IsNullOrWhiteSpace(form.ReporterEmail) ? null : form.ReporterEmail.Trim(),
            Created = _time.UtcNow,
        });
        #pragma warning restore CS8602
        await _context.SaveChangesAsync(cancellationToken);

        return ScreenResult<ReportAbuseScreen>.Ok(screen with { Submitted = true });
    }

    // The DataAnnotations of the form, in property order, keyed by property name (what model binding produced for the page).
    private static Dictionary<string, List<string>> Validate(ReportAbuseForm form)
    {
        var errors = new Dictionary<string, List<string>>();
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        foreach (var result in results)
        {
            foreach (var member in result.MemberNames.DefaultIfEmpty(string.Empty))
            {
                Add(errors, member, result.ErrorMessage ?? "The value is not valid.");
            }
        }

        return errors;
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list)) errors[key] = list = new List<string>();
        list.Add(message);
    }
}
