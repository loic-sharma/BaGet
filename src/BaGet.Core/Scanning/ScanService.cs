using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>The scoped <see cref="IScanService"/>.</summary>
public sealed class ScanService : IScanService
{
    /// <summary>The most findings kept for one version (worst first); a big image can have thousands.</summary>
    public const int MaxFindingsPerResult = 1000;

    private readonly IContext _context;
    private readonly IRepositoryContext _repository;
    private readonly IServiceProvider _services;
    private readonly IOptionsSnapshot<ScanningOptions> _options;
    private readonly IVulnerabilitySource _source;
    private readonly ITrivyRunner _trivy;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly ILogger<ScanService> _logger;
    private readonly ClusterRuntime? _cluster;

    public ScanService(
        IContext context,
        IRepositoryContext repository,
        IServiceProvider services,
        IOptionsSnapshot<ScanningOptions> options,
        IVulnerabilitySource source,
        ITrivyRunner trivy,
        SystemTime time,
        MaintenanceGate gate,
        ILogger<ScanService> logger,
        ClusterRuntime? cluster = null)
    {
        _cluster = cluster;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _trivy = trivy ?? throw new ArgumentNullException(nameof(trivy));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ScanningOptions Options => _options.Value;

    private RepositoryInfo Repository => _repository.Current;

    /// <inheritdoc/>
    public ScanEngineStatus GetEngineStatus()
    {
        if (!Options.Enabled) return new ScanEngineStatus(false, null, "Scanning is turned off (Scanning:Enabled).");
        if (_context is NullContext) return new ScanEngineStatus(false, null, "Scanning needs a relational database; this installation keeps packages in Azure Table Storage.");
        if (Repository.Kind == RepositoryKind.Virtual) return new ScanEngineStatus(false, null, "A virtual repository holds no artifacts; scan its member repositories.");

        var scanner = _services.GetFormatService<IFormatScanner>(Repository.Format);
        if (scanner == null) return new ScanEngineStatus(false, null, "This format cannot be scanned.");

        if (scanner.Engine == ScanEngine.Image)
        {
            return _trivy.IsConfigured(Options)
                ? new ScanEngineStatus(true, "Trivy", null)
                : new ScanEngineStatus(false, null, "Trivy is not configured (Scanning:TrivyPath and Scanning:TrivyRegistryHost): container images are not scanned.");
        }

        return new ScanEngineStatus(true, string.IsNullOrWhiteSpace(Options.OsvOfflinePath) ? "OSV.dev" : "OSV (offline copy)", null);
    }

    /// <inheritdoc/>
    public async Task<bool> IsStoredAsync(string name, string version, CancellationToken cancellationToken)
    {
        var scanner = _services.GetFormatService<IFormatScanner>(Repository.Format);
        if (scanner == null || _context is NullContext) return false;

        return scanner.Engine == ScanEngine.Osv
            ? (await scanner.ListComponentsAsync(name, version, cancellationToken).ConfigureAwait(false)).Count > 0
            : await scanner.GetImageReferenceAsync(name, version, cancellationToken).ConfigureAwait(false) != null;
    }

    /// <inheritdoc/>
    public async Task<ScanResult?> FindAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (_context is NullContext) return null;
        var key = Normalize(name, version);
        if (key == null) return null;

        var result = await _context.ScanResults!
            .AsNoTracking()
            .Include(r => r.Findings)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Name == key.Value.Name && r.Version == key.Value.Version, cancellationToken)
            .ConfigureAwait(false);
        result?.Findings?.Sort((a, b) =>
        {
            var bySeverity = b.Severity.CompareTo(a.Severity);
            return bySeverity != 0 ? bySeverity : string.CompareOrdinal(a.VulnerabilityId, b.VulnerabilityId);
        });
        return result;
    }

    /// <inheritdoc/>
    public async Task ForgetAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (_context is NullContext) return;
        var key = Normalize(name, version);
        if (key == null) return;

        await RemoveAsync(key.Value.Name, key.Value.Version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ScanResult?> ScanAsync(string name, string version, CancellationToken cancellationToken)
    {
        _gate.ThrowIfClosed();
        var status = GetEngineStatus();
        if (!status.Available) throw new ScanUnavailableException(status.Reason ?? "Scanning is not available.");

        var key = Normalize(name, version);
        if (key == null) return null;
        var (normalizedName, normalizedVersion) = key.Value;

        var scanner = _services.GetFormatService<IFormatScanner>(Repository.Format)!;
        IReadOnlyList<ScanHit> hits;
        string scannerName;
        try
        {
            if (scanner.Engine == ScanEngine.Osv)
            {
                scannerName = _source.Name;
                var components = await scanner.ListComponentsAsync(name, version, cancellationToken).ConfigureAwait(false);
                if (components.Count == 0)
                {
                    await RemoveAsync(normalizedName, normalizedVersion, cancellationToken).ConfigureAwait(false);
                    return null;
                }

                var answers = await _source.QueryAsync(components, cancellationToken).ConfigureAwait(false);
                hits = answers
                    .SelectMany(kv => kv.Value.Select(h => new ScanHit(h.Id, kv.Key.Name, kv.Key.Version, h.Severity, h.Summary, h.Url, h.FixedVersion)))
                    .ToList();
            }
            else
            {
                scannerName = "trivy";
                var reference = await scanner.GetImageReferenceAsync(name, version, cancellationToken).ConfigureAwait(false);
                if (reference == null)
                {
                    await RemoveAsync(normalizedName, normalizedVersion, cancellationToken).ConfigureAwait(false);
                    return null;
                }

                using var inFlight = ScanInFlight.Begin(Repository.Key, name, version, _cluster?.Store);
                hits = await _trivy.ScanImageAsync(Options, Options.TrivyRegistryHost!.Trim().TrimEnd('/') + "/" + Repository.Name + "/" + reference, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ScanUnavailableException e)
        {
            _logger.LogWarning("Scan of {Name} {Version} in {Repository} failed: {Reason}", name, version, Repository.Name, e.Message);
            var scannerOfFormat = scanner.Engine == ScanEngine.Osv ? _source.Name : "trivy";
            return await StoreFailureAsync(normalizedName, normalizedVersion, scannerOfFormat, e.Message, cancellationToken).ConfigureAwait(false);
        }

        return await StoreAsync(normalizedName, normalizedVersion, scannerName, hits, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ScanRepositoryReport> ScanRepositoryAsync(bool onlyStale, CancellationToken cancellationToken)
    {
        var status = GetEngineStatus();
        if (!status.Available) throw new ScanUnavailableException(status.Reason ?? "Scanning is not available.");

        var scanner = _services.GetFormatService<IFormatScanner>(Repository.Format)!;
        var artifacts = await scanner.ListArtifactsAsync(cancellationToken).ConfigureAwait(false);
        var existing = await _context.ScanResults!
            .AsNoTracking()
            .Select(r => new { r.Name, r.Version, r.ScannedAt, r.Attempted })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var known = existing.ToDictionary(r => (r.Name!, r.Version!));

        // Stale results: the artifact was deleted since.
        var current = new HashSet<(string, string)>();
        foreach (var artifact in artifacts)
        {
            var key = Normalize(artifact.Name, artifact.Version);
            if (key != null) current.Add((key.Value.Name, key.Value.Version));
        }

        var removed = 0;
        foreach (var gone in known.Keys.Where(k => !current.Contains(k)).ToList())
        {
            _gate.ThrowIfClosed();
            await RemoveAsync(gone.Item1, gone.Item2, cancellationToken).ConfigureAwait(false);
            removed++;
        }

        var now = _time.UtcNow;
        var interval = Options.RescanInterval;
        int scanned = 0, skipped = 0, failed = 0, withFindings = 0;
        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Normalize(artifact.Name, artifact.Version);
            if (key == null) continue;

            if (onlyStale && known.TryGetValue((key.Value.Name, key.Value.Version), out var row))
            {
                var fresh = row.ScannedAt != null && interval > TimeSpan.Zero && now - row.ScannedAt.Value < interval;
                // A failed attempt is retried after a short pause, not on every sweep.
                var retryLater = row.ScannedAt == null && now - row.Attempted < TimeSpan.FromMinutes(10);
                if (fresh || retryLater)
                {
                    skipped++;
                    continue;
                }
            }

            ScanResult? result;
            try
            {
                result = await ScanAsync(artifact.Name, artifact.Version, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not MaintenanceException)
            {
                // One bad artifact must not end the sweep of the repository (the artifacts after it would never be rescanned):
                // record the failure on it and go on.
                _logger.LogWarning(e, "Scan of {Name} {Version} in {Repository} failed", artifact.Name, artifact.Version, Repository.Name);
                result = null;

                // A save that failed half way leaves its entities tracked; they would fail every later save of the sweep.
                (_context as Microsoft.EntityFrameworkCore.DbContext)?.ChangeTracker.Clear();
                try
                {
                    await StoreFailureAsync(key.Value.Name, key.Value.Version, "scan", e.GetType().Name + ": " + e.Message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception inner) when (inner is not OperationCanceledException && inner is not MaintenanceException)
                {
                    _logger.LogWarning(inner, "The failure of {Name} {Version} could not be recorded", artifact.Name, artifact.Version);
                }

                failed++;
            }

            if (result == null) continue;
            if (result.Error != null && result.ScannedAt == null) failed++;
            else
            {
                scanned++;
                if (result.FindingCount > 0) withFindings++;
            }
        }

        return new ScanRepositoryReport(scanned, skipped, failed, withFindings, removed);
    }

    /// <inheritdoc/>
    public async Task<ScanSummary> SummarizeAsync(CancellationToken cancellationToken)
    {
        var status = GetEngineStatus();
        var bySeverity = new int[4];
        if (_context is NullContext || Repository.Kind == RepositoryKind.Virtual)
        {
            return new ScanSummary(status, 0, 0, 0, 0, bySeverity);
        }

        var scanner = _services.GetFormatService<IFormatScanner>(Repository.Format);
        var artifacts = scanner == null ? Array.Empty<ScanArtifact>() : (await scanner.ListArtifactsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        var rows = await _context.ScanResults!
            .AsNoTracking()
            .Select(r => new { r.Name, r.Version, r.ScannedAt, r.Highest, r.Error })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byKey = rows.ToDictionary(r => (r.Name!, r.Version!));

        int unscanned = 0, failed = 0, clean = 0;
        foreach (var artifact in artifacts)
        {
            var key = Normalize(artifact.Name, artifact.Version);
            if (key == null || !byKey.TryGetValue((key.Value.Name, key.Value.Version), out var row) || row.ScannedAt == null)
            {
                unscanned++;
                if (key != null && byKey.TryGetValue((key.Value.Name, key.Value.Version), out var failedRow) && failedRow.Error != null) failed++;
            }
            else if (row.Highest < 0) clean++;
            else bySeverity[Math.Min(row.Highest, 3)]++;
        }

        return new ScanSummary(status, artifacts.Length, unscanned, failed, clean, bySeverity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanResult>> ListAsync(int minSeverity, int skip, int take, CancellationToken cancellationToken)
    {
        if (_context is NullContext) return Array.Empty<ScanResult>();

        return await _context.ScanResults!
            .AsNoTracking()
            .Where(r => r.ScannedAt != null && r.Highest >= minSeverity && r.FindingCount > 0)
            .OrderByDescending(r => r.Highest)
            .ThenByDescending(r => r.FindingCount)
            .ThenBy(r => r.Name)
            .ThenBy(r => r.Version)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanHit>> CheckAsync(IReadOnlyList<ScanComponent> components, CancellationToken cancellationToken)
    {
        if (!Options.Enabled) throw new ScanUnavailableException("Scanning is turned off (Scanning:Enabled).");
        if (components.Count == 0) return Array.Empty<ScanHit>();

        var answers = await _source.QueryAsync(components, cancellationToken).ConfigureAwait(false);
        return answers
            .SelectMany(kv => kv.Value.Select(h => new ScanHit(h.Id, kv.Key.Name, kv.Key.Version, h.Severity, h.Summary, h.Url, h.FixedVersion)))
            .ToList();
    }

    /// <summary>
    /// The key a version is stored under: NuGet ids are lower-cased and versions normalized (so any spelling finds the same
    /// row on every database), others are used as given. Null when the name or version does not fit the columns.
    /// </summary>
    internal (string Name, string Version)? Normalize(string name, string version)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) return null;
        if (Repository.Format == RepositoryFormat.NuGet)
        {
            name = name.ToLowerInvariant();
            if (NuGetVersion.TryParse(version, out var parsed)) version = parsed.ToNormalizedString();
        }

        if (name.Length > ScanLimits.NameLength || version.Length > ScanLimits.VersionLength) return null;
        return (name, version);
    }

    private async Task RemoveAsync(string name, string version, CancellationToken cancellationToken)
    {
        var row = await _context.ScanResults!.FirstOrDefaultAsync(r => r.Name == name && r.Version == version, cancellationToken).ConfigureAwait(false);
        if (row == null) return;

        _context.ScanResults!.Remove(row);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (Repository.Format == RepositoryFormat.NuGet) await SyncNuGetAsync(name, version, Array.Empty<ScanHit>(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScanResult> StoreFailureAsync(string name, string version, string scanner, string message, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var row = await _context.ScanResults!.Include(r => r.Findings).FirstOrDefaultAsync(r => r.Name == name && r.Version == version, cancellationToken).ConfigureAwait(false);
            if (row == null)
            {
                row = new ScanResult { Name = name, Version = version, Scanner = scanner };
                _context.ScanResults!.Add(row);
            }

            row.Attempted = _time.UtcNow;
            row.Error = Cut(message, ScanLimits.ErrorLength);
            try
            {
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return row;
            }
            catch (DbUpdateException e) when (attempt == 0 && _context.IsUniqueConstraintViolationException(e))
            {
                (_context as DbContext)?.ChangeTracker.Clear();
            }
        }
    }

    private async Task<ScanResult> StoreAsync(string name, string version, string scanner, IReadOnlyList<ScanHit> hits, CancellationToken cancellationToken)
    {
        var distinct = hits
            .GroupBy(h => (h.Id, h.Component, h.ComponentVersion))
            .Select(g => g.OrderByDescending(h => h.Severity).First())
            .OrderByDescending(h => h.Severity)
            .ThenBy(h => h.Id, StringComparer.Ordinal)
            .ThenBy(h => h.Component, StringComparer.Ordinal)
            .ToList();
        var kept = distinct.Take(MaxFindingsPerResult).ToList();

        for (var attempt = 0; ; attempt++)
        {
            var row = await _context.ScanResults!.Include(r => r.Findings).FirstOrDefaultAsync(r => r.Name == name && r.Version == version, cancellationToken).ConfigureAwait(false);
            if (row == null)
            {
                row = new ScanResult { Name = name, Version = version };
                _context.ScanResults!.Add(row);
            }
            else if (row.Findings != null)
            {
                _context.ScanFindings!.RemoveRange(row.Findings);
            }

            var now = _time.UtcNow;
            row.Scanner = scanner;
            row.Attempted = now;
            row.ScannedAt = now;
            row.Error = null;
            row.Highest = distinct.Count == 0 ? ScanSeverities.None : distinct.Max(h => h.Severity);
            row.FindingCount = kept.Count;
            row.Findings = kept.Select(h => new ScanFinding
            {
                VulnerabilityId = Cut(h.Id, ScanLimits.IdLength),
                Component = Cut(h.Component, ScanLimits.NameLength),
                ComponentVersion = Cut(h.ComponentVersion, ScanLimits.VersionLength),
                Severity = Math.Clamp(h.Severity, 0, PackageVulnerabilityRules.MaxSeverity),
                Summary = h.Summary == null ? null : Cut(h.Summary, ScanLimits.SummaryLength),
                AdvisoryUrl = Cut(h.Url, ScanLimits.UrlLength),
                FixedVersion = h.FixedVersion == null ? null : Cut(h.FixedVersion, ScanLimits.VersionLength),
            }).ToList();

            try
            {
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException e) when (attempt == 0 && _context.IsUniqueConstraintViolationException(e))
            {
                (_context as DbContext)?.ChangeTracker.Clear();
                continue;
            }

            if (Repository.Format == RepositoryFormat.NuGet) await SyncNuGetAsync(name, version, distinct, cancellationToken).ConfigureAwait(false);
            return row;
        }
    }

    /// <summary>
    /// Feeds NuGet's vulnerability resource: the findings that are about the package version itself replace the entries the scanner
    /// recorded before (<see cref="PackageVulnerability.Source"/> <c>osv</c>); entries set by hand stay.
    /// </summary>
    private async Task SyncNuGetAsync(string lowerId, string normalizedVersion, IReadOnlyList<ScanHit> hits, CancellationToken cancellationToken)
    {
        var package = await _context.Packages!
            .Include(p => p.Vulnerabilities)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == lowerId && p.NormalizedVersionString == normalizedVersion, cancellationToken)
            .ConfigureAwait(false);
        if (package == null) return;

        var own = hits
            .Where(h => string.Equals(h.Component, package.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeVersion(h.ComponentVersion), normalizedVersion, StringComparison.OrdinalIgnoreCase))
            .Select(h => new PackageVulnerability
            {
                AdvisoryUrl = h.Url,
                Severity = Math.Clamp(h.Severity, 0, PackageVulnerabilityRules.MaxSeverity),
                VersionRange = "[" + normalizedVersion + "]",
                Source = _source.Name,
            })
            .GroupBy(v => (v.AdvisoryUrl, v.Severity))
            .Select(g => g.First())
            .ToList();

        var old = (package.Vulnerabilities ?? new List<PackageVulnerability>()).Where(v => v.Source == _source.Name).ToList();
        var unchanged = old.Count == own.Count && old.All(o => own.Any(n => n.AdvisoryUrl == o.AdvisoryUrl && n.Severity == o.Severity));
        if (unchanged) return;

        _context.PackageVulnerabilities!.RemoveRange(old);
        package.Vulnerabilities ??= new List<PackageVulnerability>();
        package.Vulnerabilities.AddRange(own);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeVersion(string version)
        => NuGetVersion.TryParse(version, out var parsed) ? parsed.ToNormalizedString() : version;

    private static string Cut(string value, int max) => value.Length <= max ? value : value.Substring(0, max);
}
