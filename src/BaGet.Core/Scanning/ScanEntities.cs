using System;
using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>
/// What the last scans of one artifact version found, in one repository (table <c>ScanResults</c>). The artifact is named the way
/// its format names it: NuGet package id (as stored), npm package name, Maven <c>group:artifact</c>, Docker image name; the
/// version is the package version, npm version, Maven version or the Docker manifest digest. A row exists once a scan was
/// attempted; there is no foreign key to the artifact (it may be deleted, and each format keeps its own tables), so stale rows are
/// dropped by the next sweep.
/// </summary>
public class ScanResult : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>The owning repository; see <see cref="IRepositoryScoped"/>.</summary>
    public int RepositoryKey { get; set; }

    /// <summary>The artifact name (see the class remarks). At most <c>ScanLimits.NameLength</c> characters.</summary>
    public string? Name { get; set; }

    /// <summary>The artifact version. At most <c>ScanLimits.VersionLength</c> characters.</summary>
    public string? Version { get; set; }

    /// <summary>The scanner that produced the findings: <c>osv</c> or <c>trivy</c>.</summary>
    public string? Scanner { get; set; }

    /// <summary>When the last scan <em>succeeded</em> (UTC); null when none ever did (the artifact counts as unscanned).</summary>
    public DateTime? ScannedAt { get; set; }

    /// <summary>When a scan was last attempted (UTC).</summary>
    public DateTime Attempted { get; set; }

    /// <summary>The scrubbed reason the last attempt failed, or null. The findings of the last success stay while this is set.</summary>
    public string? Error { get; set; }

    /// <summary>The highest severity among <see cref="Findings"/> (0 low … 3 critical), or -1 when there are none.</summary>
    public int Highest { get; set; } = -1;

    /// <summary>How many findings the last success recorded.</summary>
    public int FindingCount { get; set; }

    public List<ScanFinding>? Findings { get; set; }
}

/// <summary>One vulnerability a scan found in an artifact or one of its components.</summary>
public class ScanFinding
{
    public int Key { get; set; }

    /// <summary>The <see cref="ScanResult.Key"/>. Required; deleting the result deletes its findings.</summary>
    public int ResultKey { get; set; }

    /// <summary>The advisory id (<c>GHSA-…</c>, <c>CVE-…</c>), as the source names it.</summary>
    public string? VulnerabilityId { get; set; }

    /// <summary>The component that is vulnerable: the artifact itself or one of its dependencies (package, OS package…).</summary>
    public string? Component { get; set; }

    /// <summary>The version of <see cref="Component"/>.</summary>
    public string? ComponentVersion { get; set; }

    /// <summary>0 low, 1 moderate, 2 high, 3 critical (the numbering of NuGet's vulnerability resource).</summary>
    public int Severity { get; set; }

    /// <summary>A one-line description from the source, or null.</summary>
    public string? Summary { get; set; }

    /// <summary>Where the advisory is described (an absolute http(s) URL).</summary>
    public string? AdvisoryUrl { get; set; }

    /// <summary>The first version that fixes it, when the source says.</summary>
    public string? FixedVersion { get; set; }
}

/// <summary>
/// What a repository (or a project's repositories) does about findings: a threshold severity per action, or none (null) for
/// the action. A repository's own policy wins over its project's. Exactly one of <see cref="RepositoryKey"/> and
/// <see cref="ProjectKey"/> is set.
/// </summary>
public class ScanPolicy
{
    public int Key { get; set; }

    /// <summary>The repository this policy is for, or null for a project policy.</summary>
    public int? RepositoryKey { get; set; }

    /// <summary>The project this policy is for, or null for a repository policy.</summary>
    public int? ProjectKey { get; set; }

    /// <summary>Findings at or above this severity (0..3) are shown as a warning. Null: no warning.</summary>
    public int? WarnSeverity { get; set; }

    /// <summary>A version with a finding at or above this severity cannot be downloaded (403 with the reason). Null: never.</summary>
    public int? BlockDownloadSeverity { get; set; }

    /// <summary>A deploy whose artifact or declared dependencies have a finding at or above this severity is refused (403). Null: never.</summary>
    public int? BlockDeploySeverity { get; set; }

    /// <summary>Whether a version that has not been scanned (yet, or at all) cannot be downloaded.</summary>
    public bool BlockUnscanned { get; set; }

    /// <summary>When the policy was last saved (UTC).</summary>
    public DateTime Modified { get; set; }

    /// <summary>The administrator who saved it, or null.</summary>
    public string? ModifiedBy { get; set; }
}
