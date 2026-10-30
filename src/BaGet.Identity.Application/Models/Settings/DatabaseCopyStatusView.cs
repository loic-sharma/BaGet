using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Settings;

/// <summary>
/// The progress of the database copy as the Settings page polls it (<c>?handler=CopyStatus</c>): the properties are those the page's script reads
/// (<c>phase</c> as the phase name, <c>active</c>, <c>finished</c>, <c>succeeded</c>, <c>restartRequired</c> ...). Holds no connection string.
/// </summary>
/// <param name="Phase">The stage, by name.</param>
/// <param name="Active">A copy is queued or running.</param>
/// <param name="Finished">The copy ended, either way.</param>
/// <param name="Succeeded">The copy succeeded.</param>
/// <param name="RestartRequired">The settings are saved and the process must restart to use them.</param>
/// <param name="Table">The table being copied, or null.</param>
/// <param name="TableRowsCopied">Rows of the table copied so far.</param>
/// <param name="TableRowsTotal">Rows of the table in the source.</param>
/// <param name="TablesDone">Tables completed.</param>
/// <param name="TablesTotal">Tables to copy.</param>
/// <param name="RowsCopied">Rows copied over all tables.</param>
/// <param name="RowsTotal">Rows to copy over all tables.</param>
/// <param name="Message">A one-line summary of the current state.</param>
/// <param name="Errors">Why the copy was refused or failed.</param>
public sealed record DatabaseCopyStatusView(
    string Phase,
    bool Active,
    bool Finished,
    bool Succeeded,
    bool RestartRequired,
    string? Table,
    long TableRowsCopied,
    long TableRowsTotal,
    int TablesDone,
    int TablesTotal,
    long RowsCopied,
    long RowsTotal,
    string? Message,
    IReadOnlyList<string> Errors)
{
    /// <summary>The page's view of <paramref name="status"/>.</summary>
    public static DatabaseCopyStatusView Of(DatabaseCopyStatus status)
        => new(
            status.Phase.ToString(),
            status.IsActive,
            status.IsFinished,
            status.Phase == DatabaseCopyPhase.Succeeded,
            status.RestartRequired,
            status.Table,
            status.TableRowsCopied,
            status.TableRowsTotal,
            status.TablesDone,
            status.TablesTotal,
            status.RowsCopied,
            status.RowsTotal,
            status.Message,
            status.Errors);
}
