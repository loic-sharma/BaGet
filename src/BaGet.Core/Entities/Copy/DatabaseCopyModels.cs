using System;
using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>
/// What a database copy needs: the target and the settings to save once the copy is verified.
/// </summary>
/// <param name="TargetType">The target's <c>Database:Type</c> (a relational one).</param>
/// <param name="TargetConnectionString">The target's connection string. Never logged or shown.</param>
/// <param name="MySqlServerVersion">The target's <c>Database:MySqlServerVersion</c>, or null.</param>
/// <param name="SettingChanges">
/// The settings to save, through <see cref="SettingsService.SaveAsync"/>, after the copy is verified
/// (<c>Database:Type</c>, <c>Database:ConnectionString</c> and anything else posted with them).
/// </param>
/// <param name="Actor">Who asked, for the audit log.</param>
public sealed record DatabaseCopyRequest(
    string TargetType,
    string TargetConnectionString,
    string? MySqlServerVersion,
    IReadOnlyDictionary<SettingDescriptor, object> SettingChanges,
    string? Actor);

/// <summary>The stage a database copy is in.</summary>
public enum DatabaseCopyPhase
{
    /// <summary>No copy has been requested since startup.</summary>
    Idle,

    /// <summary>Accepted, waiting for the worker.</summary>
    Queued,

    /// <summary>Connecting to the target and checking the source.</summary>
    Checking,

    /// <summary>Creating or upgrading the target's schema.</summary>
    Migrating,

    /// <summary>Copying rows.</summary>
    Copying,

    /// <summary>Comparing row counts and key checksums.</summary>
    Verifying,

    /// <summary>Saving the new database settings.</summary>
    SavingSettings,

    /// <summary>Removing the rows copied so far, after a failure.</summary>
    RollingBack,

    /// <summary>Done; the settings are saved and a restart is needed.</summary>
    Succeeded,

    /// <summary>Failed; the source is untouched, the target emptied, no settings saved.</summary>
    Failed,
}

/// <summary>
/// A snapshot of the copy's progress, as shown by the Settings page. Holds no connection string.
/// </summary>
/// <param name="Phase">The stage.</param>
/// <param name="Table">The table being copied, or null.</param>
/// <param name="TableRowsCopied">Rows of <paramref name="Table"/> copied so far.</param>
/// <param name="TableRowsTotal">Rows of <paramref name="Table"/> in the source.</param>
/// <param name="TablesDone">Tables completed.</param>
/// <param name="TablesTotal">Tables to copy.</param>
/// <param name="RowsCopied">Rows copied over all tables.</param>
/// <param name="RowsTotal">Rows to copy over all tables.</param>
/// <param name="Errors">Why the copy was refused or failed.</param>
/// <param name="Message">A one-line summary of the current state.</param>
public sealed record DatabaseCopyStatus(
    DatabaseCopyPhase Phase,
    string? Table,
    long TableRowsCopied,
    long TableRowsTotal,
    int TablesDone,
    int TablesTotal,
    long RowsCopied,
    long RowsTotal,
    IReadOnlyList<string> Errors,
    string? Message)
{
    /// <summary>The state before any copy.</summary>
    public static DatabaseCopyStatus Idle { get; } = new(
        DatabaseCopyPhase.Idle, null, 0, 0, 0, 0, 0, 0, Array.Empty<string>(), null);

    /// <summary>A copy is queued or running.</summary>
    public bool IsActive => Phase is not (DatabaseCopyPhase.Idle or DatabaseCopyPhase.Succeeded or DatabaseCopyPhase.Failed);

    /// <summary>The copy ended, either way.</summary>
    public bool IsFinished => Phase is DatabaseCopyPhase.Succeeded or DatabaseCopyPhase.Failed;

    /// <summary>The copy succeeded: the settings are saved and the process must restart to use them.</summary>
    public bool RestartRequired => Phase == DatabaseCopyPhase.Succeeded;
}
