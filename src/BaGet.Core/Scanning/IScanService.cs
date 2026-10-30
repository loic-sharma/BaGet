using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>Whether the repository of the request can be scanned now, and by what.</summary>
/// <param name="Available">Whether a scan can run.</param>
/// <param name="Engine">What scans the repository's artifacts (<c>OSV.dev</c>, <c>OSV offline copy</c>, <c>Trivy</c>), or null.</param>
/// <param name="Reason">Why it cannot, or null. Safe to show.</param>
public sealed record ScanEngineStatus(bool Available, string? Engine, string? Reason);

/// <summary>What a pass over a repository did.</summary>
/// <param name="Scanned">Artifacts scanned.</param>
/// <param name="Skipped">Artifacts left alone because their last scan is recent enough.</param>
/// <param name="Failed">Artifacts whose scan failed (the source was unreachable…).</param>
/// <param name="WithFindings">Of the scanned, those with at least one finding.</param>
/// <param name="Removed">Results dropped because the artifact is gone.</param>
public sealed record ScanRepositoryReport(int Scanned, int Skipped, int Failed, int WithFindings, int Removed)
{
    /// <summary>A one-line description for the Operations page.</summary>
    public override string ToString()
        => $"Scanned {Scanned} artifact(s): {WithFindings} with findings, {Failed} failed, {Skipped} up to date, {Removed} stale result(s) removed.";
}

/// <summary>The repository dashboard numbers.</summary>
/// <param name="Engine">The scanner status.</param>
/// <param name="Artifacts">Artifacts stored in the repository.</param>
/// <param name="Unscanned">Artifacts with no successful scan.</param>
/// <param name="Failed">Of those, the ones whose last attempt failed.</param>
/// <param name="Clean">Artifacts scanned without findings.</param>
/// <param name="BySeverity">Artifacts by their highest finding: index 0 low … 3 critical.</param>
public sealed record ScanSummary(ScanEngineStatus Engine, int Artifacts, int Unscanned, int Failed, int Clean, IReadOnlyList<int> BySeverity);

/// <summary>
/// Scans the artifacts of the repository of the request and keeps the results. Scoped; works in
/// <see cref="IRepositoryContext.Current"/>, so a caller outside a request uses a repository scope. Authorizes nothing (callers decide).
/// </summary>
public interface IScanService
{
    /// <summary>Whether and how the repository can be scanned now.</summary>
    ScanEngineStatus GetEngineStatus();

    /// <summary>
    /// Whether the repository stores the version (a remote repository does not until the first download fetched it, so there is
    /// nothing to scan before).
    /// </summary>
    Task<bool> IsStoredAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>The stored result of a version with its findings (worst first), or null when it was never scanned.</summary>
    Task<ScanResult?> FindAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets the stored result of a version whose artifact was just stored again (a re-publish after a delete, a Maven redeploy or
    /// a new snapshot build): the old verdict is about other bytes. No-op without a result.
    /// </summary>
    Task ForgetAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Scans one version now and stores the result (and, for NuGet, the vulnerability resource's entries). A failed scan is stored
    /// as such (<see cref="ScanResult.Error"/>) and returned.
    /// </summary>
    /// <returns>The result, or null when the version does not exist (a stored result of it is removed).</returns>
    /// <exception cref="ScanUnavailableException">Scanning is off or the repository cannot be scanned.</exception>
    /// <exception cref="MaintenanceException">The database is read-only.</exception>
    Task<ScanResult?> ScanAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Scans every version of the repository (<paramref name="onlyStale"/>: only those not scanned within
    /// <see cref="ScanningOptions.RescanInterval"/>) and drops results of versions that no longer exist.
    /// </summary>
    Task<ScanRepositoryReport> ScanRepositoryAsync(bool onlyStale, CancellationToken cancellationToken);

    /// <summary>The numbers of the repository dashboard.</summary>
    Task<ScanSummary> SummarizeAsync(CancellationToken cancellationToken);

    /// <summary>Scanned versions with a finding of at least <paramref name="minSeverity"/>, worst first (no findings loaded).</summary>
    Task<IReadOnlyList<ScanResult>> ListAsync(int minSeverity, int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// The advisories that apply to <paramref name="components"/> (OSV formats), without storing anything: what a deploy check asks.
    /// </summary>
    /// <exception cref="ScanUnavailableException">The source cannot be reached.</exception>
    Task<IReadOnlyList<ScanHit>> CheckAsync(IReadOnlyList<ScanComponent> components, CancellationToken cancellationToken);
}
