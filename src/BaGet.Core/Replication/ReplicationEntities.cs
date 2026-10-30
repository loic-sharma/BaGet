using System;

namespace BaGet.Core;

/// <summary>Column limits of the replication tables; the name limit is the retention one (a Maven <c>group:artifact</c> fits).</summary>
public static class ReplicationLimits
{
    public const int MaxNameLength = RetentionLimits.MaxNameLength;
    public const int MaxVersionLength = RetentionLimits.MaxVersionLength;
    public const int MaxPartLength = 256;
    public const int MaxDigestLength = 160;
    public const int MaxUrlLength = RemoteRepositorySettings.MaxUrlLength;
    public const int MaxSecretLength = 2048;
    public const int MaxResultLength = 1024;
    public const int MaxDetailLength = 4000;
}

/// <summary>Which way a <see cref="ReplicationConfig"/> copies.</summary>
public enum ReplicationDirection
{
    /// <summary>This server's repository is the source; the peer's repository receives.</summary>
    Push = 0,

    /// <summary>The peer's repository is the source; this server's repository receives.</summary>
    Pull = 1,
}

/// <summary>What happens when both sides hold the same item with different content.</summary>
public enum ReplicationConflict
{
    /// <summary>The receiving side keeps its item; the run logs a conflict.</summary>
    TargetWins = 0,

    /// <summary>The receiving side's item is deleted and replaced by the source's.</summary>
    SourceWins = 1,
}

/// <summary>How a <see cref="ReplicationRun"/> ended.</summary>
public enum ReplicationRunStatus
{
    Running = 0,

    /// <summary>Everything the plan held was done.</summary>
    Succeeded = 1,

    /// <summary>Finished, but some items failed (see the detail) or the peer could not be used.</summary>
    Failed = 2,

    /// <summary>Stopped before the end (the server stopped, or an administrator cancelled); a later run continues from what is in place.</summary>
    Interrupted = 3,
}

/// <summary>
/// One replication relationship: a local repository and a repository of another BaGet server, copied in one direction. Not
/// repository-scoped (the administrator lists configurations across repositories). Its <c>RepositoryKey</c> is a Restrict foreign key, and
/// so is <c>ConfigKey</c> of its runs and ledger: deleting the repository deletes its configurations, runs and ledger first (code, not cascade).
/// </summary>
public class ReplicationConfig
{
    public int Key { get; set; }

    /// <summary>Unique, <see cref="AccessNames"/> rules.</summary>
    public string? Name { get; set; }

    /// <summary>The local <see cref="Repository.Key"/>: a local (not remote, not virtual) repository.</summary>
    public int RepositoryKey { get; set; }

    public ReplicationDirection Direction { get; set; }

    /// <summary>The peer server's base address (<c>https://other.example/</c>), not a repository feed.</summary>
    public string? PeerUrl { get; set; }

    /// <summary>The peer's repository (<c>default</c> or its name); it must be of the same format.</summary>
    public string? PeerRepository { get; set; }

    /// <summary>
    /// The peer's API key, protected like a remote repository's password (<see cref="IRemoteCredentialProtector"/>); never shown by a
    /// page or the API and never logged. The key needs the peer's <c>admin:api</c> scope (inventory) and the format's push or read rights.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProtectedApiKey { get; set; }

    /// <summary>A disabled configuration is never run by the schedule or a deploy; a manual run is refused.</summary>
    public bool Enabled { get; set; }

    /// <summary>Push only: a deploy to the local repository starts a run (after a short quiet period).</summary>
    public bool OnDeploy { get; set; }

    /// <summary>Run every this many minutes. Null: only when started (or on deploy).</summary>
    public int? ScheduleMinutes { get; set; }

    /// <summary>What was replicated and is later deleted at the source is deleted at the target too (only items this configuration copied).</summary>
    public bool PropagateDeletes { get; set; }

    public ReplicationConflict Conflict { get; set; }

    /// <summary>When the schedule was set or the configuration enabled; the first scheduled run is due this long after it.</summary>
    public DateTime? ScheduleStart { get; set; }

    /// <summary>When the last run started (the schedule counts from the later of this and <see cref="ScheduleStart"/>).</summary>
    public DateTime? LastRun { get; set; }

    /// <summary>One line about the last run.</summary>
    public string? LastResult { get; set; }

    public DateTime Created { get; set; }

    public DateTime Modified { get; set; }
}

/// <summary>One run of a configuration: the replication log.</summary>
public class ReplicationRun
{
    public int Key { get; set; }

    /// <summary>The <see cref="ReplicationConfig.Key"/>.</summary>
    public int ConfigKey { get; set; }

    public ReplicationRunStatus Status { get; set; }

    /// <summary><c>manual</c>, <c>resume</c>, <c>schedule</c> or <c>deploy</c>.</summary>
    public string? Trigger { get; set; }

    /// <summary>The administrator, or <c>scheduler</c>.</summary>
    public string? Actor { get; set; }

    /// <summary>The interrupted or failed run this one continues, when started with <em>Resume</em>.</summary>
    public int? ResumedFrom { get; set; }

    public DateTime Started { get; set; }

    public DateTime? Finished { get; set; }

    /// <summary>Items the plan looked at (both sides listed).</summary>
    public int Examined { get; set; }

    /// <summary>Items copied to the target.</summary>
    public int Transferred { get; set; }

    /// <summary>Items the target already had.</summary>
    public int Skipped { get; set; }

    /// <summary>Items deleted at the target because they were deleted at the source.</summary>
    public int Deleted { get; set; }

    /// <summary>Items held differently on both sides that were left alone (target wins) or replaced (source wins).</summary>
    public int Conflicts { get; set; }

    public int Failures { get; set; }

    /// <summary>Bytes of artifact content sent or received.</summary>
    public long Bytes { get; set; }

    /// <summary>Why the run failed as a whole, or null.</summary>
    public string? Error { get; set; }

    /// <summary>The first failures and conflicts, one per line.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// An item a configuration copied: the ledger that tells "deleted at the source since" from "never copied", so delete propagation
/// removes only what replication put there. Not repository-scoped.
/// </summary>
public class ReplicationItem
{
    public int Key { get; set; }

    public int ConfigKey { get; set; }

    public string? Name { get; set; }

    public string? Version { get; set; }

    /// <summary>The file of a Maven version; empty for the other formats.</summary>
    public string? Part { get; set; }

    /// <summary>The content digest at the time of the copy, or empty.</summary>
    public string? Digest { get; set; }

    public DateTime Synced { get; set; }
}
