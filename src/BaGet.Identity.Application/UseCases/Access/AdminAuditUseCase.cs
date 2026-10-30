using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Models.Access;

namespace BaGet.Identity.Application.UseCases.Access;

/// <summary>
/// The access audit log: one entry per change to groups, memberships, permission targets, grants, projects, the repositories' project and
/// anonymous-read switch, newest first, filtered by who made the change, what it was done to and the dates (UTC, <c>yyyy-MM-dd</c>, both inclusive).
/// Read only; server administrators only (the gate decides).
/// </summary>
public sealed class AdminAuditUseCase
{
    /// <summary>How many entries the screen lists at most.</summary>
    public const int PageSize = 200;

    private readonly IAccessAdministration _access;

    /// <summary>Creates the use case.</summary>
    public AdminAuditUseCase(IAccessAdministration access)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    /// <summary>Lists the entries that match the filter.</summary>
    public async Task<ScreenResult<AuditViewModel>> GetAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var model = new AuditViewModel { ActorFilter = query.Actor, Subject = query.Subject, From = query.From, To = query.To };
        var from = ParseDay(model, model.From, "From", 0);
        var to = ParseDay(model, model.To, "To", 1);

        model.Entries = await _access.AuditAsync(new AuditFilter(model.ActorFilter, model.Subject, from, to), PageSize, cancellationToken);

        return ScreenResult<AuditViewModel>.Ok(model);
    }

    private static DateTime? ParseDay(AuditViewModel model, string? text, string field, int addDays)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day))
        {
            return day.AddDays(addDays);
        }

        model.FilterError = $"{field} is not a date (use yyyy-MM-dd).";
        return null;
    }
}
