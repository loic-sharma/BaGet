using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>What a policy refused.</summary>
public enum ScanBlockedOperation
{
    /// <summary>A download of a version with findings (or not scanned).</summary>
    Download,

    /// <summary>A deploy of an artifact with findings.</summary>
    Deploy,
}

/// <summary>
/// The scanning policy of a repository refuses an operation. Answered as <c>403</c> with <see cref="Exception.Message"/> as the
/// reason on every format's routes and pages (<c>ScanBlockedFilter</c>).
/// </summary>
public sealed class ScanBlockedException : Exception
{
    public ScanBlockedException(ScanBlockedOperation operation, string reason)
        : base(reason)
    {
        Operation = operation;
    }

    /// <summary>What was refused.</summary>
    public ScanBlockedOperation Operation { get; }
}

/// <summary>The state of one version as the pages show it.</summary>
public enum ScanState
{
    /// <summary>Scanning is off or not possible for the repository (see <see cref="ScanAssessment.Note"/>).</summary>
    Unavailable,

    /// <summary>No successful scan yet.</summary>
    NotScanned,

    /// <summary>Scanned, nothing found.</summary>
    Clean,

    /// <summary>Scanned, at least one finding.</summary>
    Findings,
}

/// <summary>What the pages know about a version: its result and what the policy does with it.</summary>
/// <param name="State">The state.</param>
/// <param name="Result">The stored result with its findings, or null.</param>
/// <param name="Policy">The policy in force.</param>
/// <param name="Warn">Whether the policy asks for a warning.</param>
/// <param name="BlockReason">Why downloads are refused, or null when they are not.</param>
/// <param name="Note">Why the version cannot be scanned or has not been, when that is known.</param>
public sealed record ScanAssessment(ScanState State, ScanResult? Result, EffectiveScanPolicy Policy, bool Warn, string? BlockReason, string? Note);

/// <summary>
/// Applies the scanning policy of the repository of the request. Scoped. A format's routes and pages call
/// <see cref="CheckDownloadAsync"/> before they hand an artifact out and <see cref="CheckDeployAsync"/> before they store one;
/// both are free (one cached lookup) when no policy asks for anything.
/// </summary>
public interface IScanGate
{
    /// <summary>Whether scanning is on and possible here (a cheap test; callers skip reading an artifact for scanning when false).</summary>
    bool Enabled { get; }

    /// <summary>Whether the policy in force refuses deploys at some severity (callers read the artifact's components only then).</summary>
    Task<bool> BlocksDeploysAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether a pull of the image is the scanning engine's own (Trivy pulls the image it scans through this registry): the caller is
    /// <see cref="ScanningOptions.TrivyRegistryUser"/> (exempt from <see cref="CheckDownloadAsync"/> entirely, so rescans of images
    /// with findings still work), or, when no user is configured and so the scanner cannot be told from any other client, a scan of
    /// that very image is running <b>and the image has no successful scan yet</b>. The second case only lets an unscanned image be
    /// scanned; it never lifts the severity block of an image already known to be vulnerable.
    /// </summary>
    Task<bool> IsScannerPullAsync(string name, string version, string? callerName, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="ScanBlockedException"/> when the policy refuses to hand out the version.</summary>
    Task CheckDownloadAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Throws <see cref="ScanBlockedException"/> when the policy refuses to store an artifact made of
    /// <paramref name="components"/> (itself and its declared dependencies). A source that cannot answer lets the deploy through
    /// (logged): a security tool that is down must not stop every push.
    /// </summary>
    Task CheckDeployAsync(IReadOnlyList<ScanComponent> components, CancellationToken cancellationToken);

    /// <summary>
    /// For artifacts that cannot be inspected before they are stored (container images): scans the stored version now and throws
    /// <see cref="ScanBlockedException"/> when its findings reach the policy's deploy threshold, so the caller can remove it again.
    /// Without a deploy threshold, or without a scanner for the format, it only queues a scan.
    /// </summary>
    Task CheckStoredAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>The state and the policy's verdict for a version, for the pages.</summary>
    Task<ScanAssessment> AssessAsync(string name, string version, CancellationToken cancellationToken);

    /// <summary>Queues a scan of a version that was just stored (no-op when scanning is off).</summary>
    void RequestScan(string name, string version);

    /// <summary>
    /// What every store path calls once an artifact is stored: forgets the result of an earlier artifact of the same name and
    /// version (it describes other bytes, and would count as a scan for "block unscanned") and then <see cref="RequestScan"/>.
    /// A failure to forget is logged, never thrown: the artifact is already stored. <paramref name="contentChanged"/> false (a file the
    /// scanner does not read, e.g. a Maven classifier jar) keeps the result and only queues a scan.
    /// </summary>
    Task RegisterStoredAsync(string name, string version, CancellationToken cancellationToken, bool contentChanged = true);
}

/// <summary>The scoped <see cref="IScanGate"/>.</summary>
public sealed class ScanGate : IScanGate
{
    private readonly IScanService _scans;
    private readonly IScanPolicyService _policies;
    private readonly IRepositoryContext _repository;
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsSnapshot<ScanningOptions> _options;
    private readonly ScanQueue _queue;
    private readonly IContext _context;
    private readonly IVirtualRepositories _virtuals;
    private readonly ILogger<ScanGate> _logger;
    private readonly ClusterRuntime? _cluster;
    private readonly VirtualRepositoryReader? _reader;
    private readonly IOciRequestAccess? _ociAccess;

    public ScanGate(
        IScanService scans,
        IScanPolicyService policies,
        IRepositoryContext repository,
        IServiceScopeFactory scopes,
        IOptionsSnapshot<ScanningOptions> options,
        ScanQueue queue,
        IContext context,
        IVirtualRepositories virtuals,
        ILogger<ScanGate> logger,
        ClusterRuntime? cluster = null,
        VirtualRepositoryReader? reader = null,
        IOciRequestAccess? ociAccess = null)
    {
        _ociAccess = ociAccess;
        _cluster = cluster;
        _reader = reader;
        _scans = scans ?? throw new ArgumentNullException(nameof(scans));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private bool Active => _options.Value.Enabled && _context is not NullContext;

    /// <inheritdoc/>
    public bool Enabled => Active;

    /// <inheritdoc/>
    public async Task<bool> BlocksDeploysAsync(CancellationToken cancellationToken)
        => Active && (await _policies.GetEffectiveAsync(_repository.Current.Key, cancellationToken).ConfigureAwait(false)).Values.BlockDeploySeverity != null;

    /// <inheritdoc/>
    public async Task<bool> IsScannerPullAsync(string name, string version, string? callerName, CancellationToken cancellationToken)
    {
        if (!Active) return false;

        var user = _options.Value.TrivyRegistryUser;
        if (!string.IsNullOrWhiteSpace(user))
        {
            return callerName != null && string.Equals(callerName, user.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        if (!await ScanInFlight.ContainsAsync(_repository.Current.Key, name, version, _cluster?.Store, cancellationToken).ConfigureAwait(false)) return false;

        // Any client could be the one pulling during the scan, so only the unscanned case (the "block unscanned" check) is lifted.
        var result = await _scans.FindAsync(name, version, cancellationToken).ConfigureAwait(false);
        return result?.ScannedAt == null;
    }

    /// <inheritdoc/>
    public async Task CheckDownloadAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (!Active) return;

        var repository = _repository.Current;
        if (repository.Kind == RepositoryKind.Virtual)
        {
            // The member that holds the version answers; one with no result for it is unscanned and says nothing. Only the members
            // the caller can read count, as on the content path (which serves the first of them): judging a member the caller cannot
            // read would let another member's copy through on its verdict.
            // A Docker pull is judged like the registry judges it (IOciRequestAccess: the registry token's grants or the key), since
            // the caller of the reader cannot see a registry token and would look anonymous.
            IReadOnlyList<RepositoryInfo> members;
            if (repository.Format == RepositoryFormat.Docker && _ociAccess != null)
            {
                var all = await _virtuals.EffectiveMembersAsync(repository, cancellationToken).ConfigureAwait(false);
                var allowed = new List<RepositoryInfo>();
                foreach (var candidate in all)
                {
                    if (await _ociAccess.CanPullAsync(candidate, name, cancellationToken).ConfigureAwait(false)) allowed.Add(candidate);
                }

                members = allowed;
            }
            else
            {
                members = _reader != null
                    ? await _reader.ReadableMembersAsync(name, cancellationToken).ConfigureAwait(false)
                    : await _virtuals.EffectiveMembersAsync(repository, cancellationToken).ConfigureAwait(false);
            }

            foreach (var member in members)
            {
                using var scope = _scopes.CreateRepositoryScope(member);
                var memberGate = (ScanGate)scope.ServiceProvider.GetRequiredService<IScanGate>();
                if (await memberGate.CheckDownloadCoreAsync(name, version, requireResult: true, cancellationToken).ConfigureAwait(false)) return;
            }

            return;
        }

        await CheckDownloadCoreAsync(name, version, requireResult: false, cancellationToken).ConfigureAwait(false);
    }

    /// <returns>True when this repository had a result for the version (virtual lookups stop at the first that does).</returns>
    private async Task<bool> CheckDownloadCoreAsync(string name, string version, bool requireResult, CancellationToken cancellationToken)
    {
        var repository = _repository.Current;
        var policy = await _policies.GetEffectiveAsync(repository.Key, cancellationToken).ConfigureAwait(false);
        var values = policy.Values;
        var result = await _scans.FindAsync(name, version, cancellationToken).ConfigureAwait(false);

        // A virtual lookup stops at the member that holds the version: one with a result, or one that stores it without having
        // scanned it (its own "block unscanned" then applies).
        var stored = result != null;
        async Task<bool> HoldsAsync()
        {
            if (!stored) stored = await _scans.IsStoredAsync(name, version, cancellationToken).ConfigureAwait(false);
            return stored;
        }

        if (values.BlockDownloadSeverity == null && !values.BlockUnscanned)
        {
            return requireResult && await HoldsAsync().ConfigureAwait(false);
        }

        if (result?.ScannedAt == null)
        {
            if (requireResult && !await HoldsAsync().ConfigureAwait(false)) return false;
            Enqueue(name, version);

            // Without an engine nothing can ever be scanned, so refusing would be for good (the pages say nothing is blocked then).
            // What a remote repository has not fetched yet cannot be scanned, and it is fetched by this very download: refusing it
            // would keep it from ever being scanned. It is scanned as soon as it is stored.
            if (values.BlockUnscanned && _scans.GetEngineStatus().Available && await HoldsAsync().ConfigureAwait(false))
            {
                throw new ScanBlockedException(ScanBlockedOperation.Download,
                    $"Download of {name} {version} refused by the scanning policy of {Describe(policy, repository)}: it has not been scanned yet. Try again shortly.");
            }

            return stored;
        }

        var reason = DownloadReason(name, version, result, policy, repository);
        if (reason != null) throw new ScanBlockedException(ScanBlockedOperation.Download, reason);
        return true;
    }

    /// <inheritdoc/>
    public async Task CheckDeployAsync(IReadOnlyList<ScanComponent> components, CancellationToken cancellationToken)
    {
        if (!Active || components.Count == 0) return;

        var repository = _repository.Current;
        var policy = await _policies.GetEffectiveAsync(repository.Key, cancellationToken).ConfigureAwait(false);
        var threshold = policy.Values.BlockDeploySeverity;
        if (threshold == null) return;

        IReadOnlyList<ScanHit> hits;
        try
        {
            hits = await _scans.CheckAsync(components, cancellationToken).ConfigureAwait(false);
        }
        catch (ScanUnavailableException e)
        {
            _logger.LogWarning("Deploy to {Repository} not checked against the scanning policy: {Reason}", repository.Name, e.Message);
            return;
        }

        var blocking = hits.Where(h => h.Severity >= threshold.Value).OrderByDescending(h => h.Severity).ThenBy(h => h.Id, StringComparer.Ordinal).ToList();
        if (blocking.Count == 0) return;

        throw new ScanBlockedException(ScanBlockedOperation.Deploy,
            $"Deploy refused by the scanning policy of {Describe(policy, repository)} (blocks {ScanSeverities.Name(threshold.Value)} and above): {Summarize(blocking)}.");
    }

    /// <inheritdoc/>
    public async Task CheckStoredAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (!Active) return;

        var repository = _repository.Current;
        var policy = await _policies.GetEffectiveAsync(repository.Key, cancellationToken).ConfigureAwait(false);
        var threshold = policy.Values.BlockDeploySeverity;
        if (threshold == null || !_scans.GetEngineStatus().Available)
        {
            RequestScan(name, version);
            return;
        }


        var result = await _scans.ScanAsync(name, version, cancellationToken).ConfigureAwait(false);
        if (result?.ScannedAt == null || result.Highest < threshold.Value) return;

        var blocking = (result.Findings ?? new List<ScanFinding>())
            .Where(f => f.Severity >= threshold.Value)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.VulnerabilityId, StringComparer.Ordinal)
            .Select(f => new ScanHit(f.VulnerabilityId!, f.Component!, f.ComponentVersion!, f.Severity, f.Summary, f.AdvisoryUrl ?? string.Empty, f.FixedVersion))
            .ToList();
        throw new ScanBlockedException(ScanBlockedOperation.Deploy,
            $"Deploy refused by the scanning policy of {Describe(policy, repository)} (blocks {ScanSeverities.Name(threshold.Value)} and above): {Summarize(blocking)}.");
    }

    /// <inheritdoc/>
    public async Task<ScanAssessment> AssessAsync(string name, string version, CancellationToken cancellationToken)
    {
        var repository = _repository.Current;
        var status = _scans.GetEngineStatus();
        var policy = _context is NullContext ? EffectiveScanPolicy.None : await _policies.GetEffectiveAsync(repository.Key, cancellationToken).ConfigureAwait(false);
        var result = _context is NullContext ? null : await _scans.FindAsync(name, version, cancellationToken).ConfigureAwait(false);

        if (result?.ScannedAt == null)
        {
            var note = result?.Error != null ? "The last scan failed: " + result.Error : status.Available ? null : status.Reason;
            string? block = null;
            if (Active && status.Available && policy.Values.BlockUnscanned)
            {
                block = $"Not scanned yet; the policy of {Describe(policy, repository)} refuses downloads of unscanned versions.";
            }

            return new ScanAssessment(status.Available || result?.Error != null ? ScanState.NotScanned : ScanState.Unavailable, result, policy, false, block, note);
        }

        var warn = policy.Values.WarnSeverity != null && result.Highest >= policy.Values.WarnSeverity.Value;
        var reason = Active ? DownloadReason(name, version, result, policy, repository) : null;
        return new ScanAssessment(result.FindingCount > 0 ? ScanState.Findings : ScanState.Clean, result, policy, warn, reason, result.Error != null ? "The last rescan failed: " + result.Error : null);
    }

    /// <inheritdoc/>
    public void RequestScan(string name, string version)
    {
        if (Active && _options.Value.ScanOnDeploy) Enqueue(name, version);
    }

    /// <inheritdoc/>
    public async Task RegisterStoredAsync(string name, string version, CancellationToken cancellationToken, bool contentChanged = true)
    {
        if (Active && contentChanged)
        {
            try
            {
                await _scans.ForgetAsync(name, version, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "The earlier scan result of {Name} {Version} could not be forgotten", name, version);
            }
        }

        RequestScan(name, version);
    }

    private void Enqueue(string name, string version)
    {
        if (Active) _queue.Enqueue(_repository.Current, name, version);
    }

    private static string? DownloadReason(string name, string version, ScanResult result, EffectiveScanPolicy policy, RepositoryInfo repository)
    {
        var threshold = policy.Values.BlockDownloadSeverity;
        if (threshold == null || result.Highest < threshold.Value) return null;

        var blocking = (result.Findings ?? new List<ScanFinding>())
            .Where(f => f.Severity >= threshold.Value)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.VulnerabilityId, StringComparer.Ordinal)
            .Select(f => new ScanHit(f.VulnerabilityId!, f.Component!, f.ComponentVersion!, f.Severity, f.Summary, f.AdvisoryUrl ?? string.Empty, f.FixedVersion))
            .ToList();
        var detail = blocking.Count > 0 ? Summarize(blocking) : $"its highest finding is {ScanSeverities.Name(result.Highest)}";
        return $"Download of {name} {version} refused by the scanning policy of {Describe(policy, repository)} (blocks {ScanSeverities.Name(threshold.Value)} and above): {detail}.";
    }

    private static string Describe(EffectiveScanPolicy policy, RepositoryInfo repository)
        => policy.From == ScanPolicyScope.Project ? $"repository '{repository.Name}' (project '{policy.Name}')" : $"repository '{repository.Name}'";

    private static string Summarize(IReadOnlyList<ScanHit> hits)
    {
        var shown = hits.Take(3).Select(h => $"{h.Component} {h.ComponentVersion}: {ScanSeverities.Name(h.Severity)} {h.Id}").ToList();
        var more = hits.Count > shown.Count ? $" and {hits.Count - shown.Count} more" : string.Empty;
        return string.Join("; ", shown) + more;
    }
}
