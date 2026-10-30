using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>An abuse report with the name of its repository.</summary>
public sealed record AbuseReportSummary(
    int Key,
    string Repository,
    string? PackageId,
    string? PackageVersion,
    string? Reason,
    string? Details,
    string? ReporterEmail,
    DateTime Created,
    DateTime? Resolved)
{
    public bool IsResolved => Resolved != null;
}

/// <summary>
/// The administrator's view of abuse reports across every repository. Needs a relational database: with Azure Table
/// (<see cref="NullContext"/>) nothing is stored, so <see cref="IsSupported"/> is false and the list is empty.
/// </summary>
public interface IAbuseReportAdministration
{
    bool IsSupported { get; }

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<AbuseReportSummary>> ListAsync(bool unresolvedOnly, CancellationToken cancellationToken);

    /// <summary>Marks a report resolved (or open again).</summary>
    /// <returns>False when there is no such report.</returns>
    /// <exception cref="MaintenanceException">The gate is closed.</exception>
    Task<bool> SetResolvedAsync(int key, bool resolved, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class AbuseReportAdministration : IAbuseReportAdministration
{
    private readonly IContext _context;
    private readonly IRepositoryService _repositories;
    private readonly SystemTime _time;
    private readonly MaintenanceGate? _gate;

    public AbuseReportAdministration(IContext context, IRepositoryService repositories, SystemTime time, MaintenanceGate? gate = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate;
    }

    /// <inheritdoc />
    public bool IsSupported => _context is not NullContext && _context.AbuseReports != null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AbuseReportSummary>> ListAsync(bool unresolvedOnly, CancellationToken cancellationToken)
    {
        if (!IsSupported) return Array.Empty<AbuseReportSummary>();

        // Every repository's reports: the page is not tied to one.
        var query = _context.AbuseReports!.IgnoreQueryFilters().AsNoTracking();
        if (unresolvedOnly) query = query.Where(r => r.Resolved == null);

        var reports = await query.OrderByDescending(r => r.Created).ThenByDescending(r => r.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = (await _repositories.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Key, r => r.Name);

        return reports
            .Select(r => new AbuseReportSummary(
                r.Key,
                names.TryGetValue(r.RepositoryKey, out var name) ? name : Repository.DefaultName,
                r.PackageId, r.PackageVersion, r.Reason, r.Details, r.ReporterEmail, r.Created, r.Resolved))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<bool> SetResolvedAsync(int key, bool resolved, CancellationToken cancellationToken)
    {
        _gate?.ThrowIfClosed();
        if (!IsSupported) return false;

        var report = await _context.AbuseReports!.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Key == key, cancellationToken).ConfigureAwait(false);
        if (report == null) return false;

        report.Resolved = resolved ? report.Resolved ?? _time.UtcNow : null;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}
