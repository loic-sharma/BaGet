using System;

namespace BaGet.Core;

/// <summary>
/// Vulnerability scanning (<c>Scanning:*</c>; user docs <c>docs/vulnerability-scanning.md</c>). NuGet, npm and Maven artifacts are
/// checked against OSV.dev (online API, or an offline copy of its data); Docker images by an optional Trivy. Everything is read
/// on each use, so a change applies without a restart. Off by default: scanning contacts the internet.
/// </summary>
public class ScanningOptions
{
    /// <summary>
    /// Whether artifacts are scanned (on deploy, on the schedule and by <em>Scan now</em>). Off: nothing is scanned, no
    /// policy blocks anything and the UI says so.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether a version is queued for a scan as soon as it is stored (published, deployed, pushed, or cached by a remote
    /// repository). Default on. Off: versions are scanned by the schedule and by <em>Scan now</em> only (a download that finds a
    /// version unscanned under a policy still queues it).
    /// </summary>
    public bool ScanOnDeploy { get; set; } = true;

    /// <summary>
    /// The OSV API root. Default <c>https://api.osv.dev</c>. Data served by OSV.dev is licensed per source (mostly CC-BY 4.0,
    /// GitHub Advisory Database CC-BY 4.0); advisories shown in the UI carry their identifier and a link to the source.
    /// </summary>
    public string OsvUrl { get; set; } = "https://api.osv.dev";

    /// <summary>
    /// A directory (searched recursively for <c>*.json</c>) or a <c>.zip</c> of OSV records, e.g. the per-ecosystem
    /// <c>all.zip</c> from <c>https://osv-vulnerabilities.storage.googleapis.com/&lt;ecosystem&gt;/all.zip</c>. When set the
    /// scanner uses it instead of the online API and never contacts the internet. Reloaded when the file or directory changes.
    /// </summary>
    public string? OsvOfflinePath { get; set; }

    /// <summary>Timeout of one OSV request. Default 30 seconds.</summary>
    public TimeSpan OsvTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long an OSV answer for a component is reused before it is asked again. Default 1 hour.</summary>
    public TimeSpan OsvCacheLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The age after which the background worker scans an artifact again, to learn of advisories published after the first scan.
    /// Zero or negative: artifacts are scanned on deploy and on demand only. Default 24 hours.
    /// </summary>
    public TimeSpan RescanInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The path of the <c>trivy</c> executable used for Docker images. Without it Docker images are not scanned and the UI marks
    /// them as unscanned.
    /// </summary>
    public string? TrivyPath { get; set; }

    /// <summary>
    /// A Trivy server (<c>trivy server</c>), passed as <c>--server</c> so the vulnerability database and the analysis live
    /// there; the executable in <see cref="TrivyPath"/> is still what is run.
    /// </summary>
    public string? TrivyServerUrl { get; set; }

    /// <summary>
    /// The registry host (and port) Trivy pulls images from: this server as the Docker client sees it, e.g.
    /// <c>registry.example.com</c>. Required for Docker scanning; images are named <c>{host}/{repository}/{image}@{digest}</c>.
    /// </summary>
    public string? TrivyRegistryHost { get; set; }

    /// <summary>Pull over plain HTTP (<c>--insecure</c>), for a registry without TLS.</summary>
    public bool TrivyInsecure { get; set; }

    /// <summary>
    /// The user name Trivy logs in with when the registry needs credentials (accounts enabled). Also what identifies the scanner's own
    /// pull to the scan gate: without it the pull is recognised from the scans running in this process only, so a deployment with several
    /// nodes must set it.
    /// </summary>
    public string? TrivyRegistryUser { get; set; }

    /// <summary>The API key Trivy logs in with (passed through the environment, never on the command line).</summary>
    [SensitiveSetting]
    public string? TrivyRegistryPassword { get; set; }

    /// <summary>The longest a Trivy run may take. Default 10 minutes.</summary>
    public TimeSpan TrivyTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
