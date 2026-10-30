using System;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A retention policy, as the API takes it. The cache limit is in bytes.</summary>
public sealed class RetentionPolicyRequest
{
    /// <summary>The policy's name.</summary>
    public string? Name { get; set; }

    /// <summary><c>repository</c> (the default) or <c>project</c>.</summary>
    public string? ScopeKind { get; set; }

    /// <summary>The repository or project it applies to.</summary>
    public string? ScopeName { get; set; }

    /// <summary>A disabled policy only reports (dry runs).</summary>
    public bool Enabled { get; set; }

    /// <summary>Package/image name globs; empty = all.</summary>
    public string? NamePatterns { get; set; }

    /// <summary>Keep the newest N versions.</summary>
    public int? KeepLast { get; set; }

    /// <summary>Delete versions older than N days.</summary>
    public int? OlderThanDays { get; set; }

    /// <summary>Delete versions nobody downloaded for N days.</summary>
    public int? UnusedDays { get; set; }

    /// <summary>Touch prerelease versions only.</summary>
    public bool PrereleaseOnly { get; set; }

    /// <summary>A remote repository's cache limit in bytes.</summary>
    public long? CacheLimitBytes { get; set; }

    /// <summary>Delete untagged Docker manifests after N days.</summary>
    public int? UntaggedAfterDays { get; set; }

    /// <summary>Run the Docker garbage collection.</summary>
    public bool CollectGarbage { get; set; }

    /// <summary>Run every N hours (the scheduler); none = only on request.</summary>
    public int? ScheduleHours { get; set; }

    internal RetentionPolicyInput ToInput(int key)
    {
        var project = string.Equals(ScopeKind, "project", StringComparison.OrdinalIgnoreCase);

        return new RetentionPolicyInput(
            key, Name, project ? null : ScopeName, project ? ScopeName : null, Enabled, NamePatterns,
            KeepLast, OlderThanDays, UnusedDays, PrereleaseOnly, CacheLimitBytes, UntaggedAfterDays, CollectGarbage, ScheduleHours);
    }
}
