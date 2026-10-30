using System;

namespace BaGet.Core;

/// <summary>Column limits of the retention tables; the longest Maven <c>group:artifact</c> (200 + 1 + 128) fits a name.</summary>
public static class RetentionLimits
{
    public const int MaxNameLength = 340;
    public const int MaxVersionLength = 128;
    public const int MaxReasonLength = 512;
    public const int MaxPatternsLength = 1024;
    public const int MaxResultLength = 1024;
}

/// <summary>
/// A retention policy: which stored versions of one repository (or of every repository of a project) are deleted, and when.
/// A version is deleted when <b>any</b> enabled rule selects it and no <see cref="RetentionHold"/> covers it. Not repository-scoped:
/// the administrator lists policies across repositories. <c>RepositoryKey</c> and <c>ProjectKey</c> are optional Restrict foreign keys;
/// deleting a repository or a project deletes its policies first.
/// </summary>
public class RetentionPolicy
{
    public int Key { get; set; }

    /// <summary>Unique, <see cref="AccessNames"/> rules.</summary>
    public string? Name { get; set; }

    /// <summary>The <see cref="Repository.Key"/> the policy applies to, or null when it applies to a project.</summary>
    public int? RepositoryKey { get; set; }

    /// <summary>The <see cref="Project.Key"/>: the policy applies to every repository of the project. Exactly one of the two keys is set.</summary>
    public int? ProjectKey { get; set; }

    /// <summary>A disabled policy is never run by the schedule and refuses a manual run (a dry run still reports).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Globs (<see cref="AccessPatterns"/>, one per line) naming the artifacts the policy looks at (package ids, npm names, Maven
    /// <c>group:artifact</c>, image names); empty means every artifact.
    /// </summary>
    public string? NamePatterns { get; set; }

    /// <summary>Keep this many newest versions of each artifact (by publish time); older ones are selected. Null: rule off.</summary>
    public int? KeepLast { get; set; }

    /// <summary>Select versions published more than this many days ago. Null: rule off.</summary>
    public int? OlderThanDays { get; set; }

    /// <summary>Select versions not downloaded for this many days (the publish time counts when never downloaded). Null: rule off.</summary>
    public int? UnusedDays { get; set; }

    /// <summary>The age and count rules look at prerelease versions (NuGet pre-release, npm prerelease, Maven snapshot) only.</summary>
    public bool PrereleaseOnly { get; set; }

    /// <summary>
    /// Remote repositories only: when what the repository cached is larger than this many bytes, the least recently used versions are
    /// selected until it fits. Null: rule off.
    /// </summary>
    public long? CacheLimitBytes { get; set; }

    /// <summary>
    /// Docker only: select manifests no tag, index or referrer relationship points at that were pushed more than this many days ago.
    /// Null: rule off.
    /// </summary>
    public int? UntaggedAfterDays { get; set; }

    /// <summary>Docker only: run the garbage collection (<see cref="IOciGarbageCollector"/>) after the rules, to free the blobs.</summary>
    public bool CollectGarbage { get; set; }

    /// <summary>Run every this many hours (a real run, by the background worker). Null: only when an administrator starts it.</summary>
    public int? ScheduleHours { get; set; }

    /// <summary>When the schedule was set or the policy enabled; the first scheduled run is due this long after it.</summary>
    public DateTime? ScheduleStart { get; set; }

    /// <summary>When the last real run started (the schedule counts from the later of this and <see cref="ScheduleStart"/>).</summary>
    public DateTime? LastRun { get; set; }

    /// <summary>One line about the last real run.</summary>
    public string? LastResult { get; set; }

    public DateTime Created { get; set; }

    /// <summary>
    /// Since when the unused rule has been watching the policy's scope (set when the rule is enabled or changed, or the repository,
    /// project or name patterns change; null on rows that predate it, which fall back to <see cref="Created"/>). A version never
    /// downloaded counts as unused from the later of its publication and this time: downloads are recorded only while watched.
    /// </summary>
    public DateTime? UnusedSince { get; set; }

    public DateTime Modified { get; set; }
}

/// <summary>
/// A legal hold: an artifact (<see cref="Version"/> empty) or one version of it that no retention policy deletes, however old or
/// unused. Repository-scoped. The name and version are in the form the format's hooks list them
/// (<see cref="IFormatRetentionHooks.Canonical"/>). Retention is all a hold governs: a user who may delete a version still can.
/// </summary>
public class RetentionHold : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>The owning repository; see <see cref="IRepositoryScoped"/>.</summary>
    public int RepositoryKey { get; set; }

    /// <summary>The artifact name as the format lists it.</summary>
    public string? Name { get; set; }

    /// <summary>The version, or the empty string for every version of the artifact (the column is never null so the unique index works).</summary>
    public string? Version { get; set; }

    /// <summary>Why (a case number, a ticket).</summary>
    public string? Reason { get; set; }

    /// <summary>The administrator who placed it.</summary>
    public string? CreatedBy { get; set; }

    public DateTime Created { get; set; }
}

/// <summary>
/// When a version was last downloaded, for the "unused" and cache rules; formats other than NuGet keep no download count. Written in
/// batches by <see cref="ArtifactUsageRecorder"/>. Repository-scoped.
/// </summary>
public class ArtifactUsage : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>The owning repository; see <see cref="IRepositoryScoped"/>.</summary>
    public int RepositoryKey { get; set; }

    /// <summary>The artifact name as the format lists it.</summary>
    public string? Name { get; set; }

    /// <summary>The version (a Docker tag or manifest digest for images).</summary>
    public string? Version { get; set; }

    /// <summary>The last download (UTC).</summary>
    public DateTime LastUsed { get; set; }
}
