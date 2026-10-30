namespace BaGet.Core;

/// <summary>
/// The operator-facing keys of the host's <c>Serilog</c> section (see "Logging" in docs/configuration.md), so the
/// Settings page can edit them. The section holds more: the <c>Override</c> levels per namespace are edited by
/// the page's "Log levels per source" panel (<see cref="SettingsService.SetLogLevelOverrideAsync"/>), while the
/// <c>WriteTo</c> sinks and their arguments are arrays the Settings page cannot show, so they stay in the
/// configuration file or the environment. The host reads the levels through Serilog's own configuration reload, which
/// is why <see cref="MinimumLevelOptions.Default"/> applies without a restart and the event log keys do not.
/// </summary>
public class SerilogOptions
{
    /// <summary>The lowest level logged.</summary>
    public MinimumLevelOptions MinimumLevel { get; set; } = new();

    /// <summary>The Windows Application event log sink (Windows only).</summary>
    public EventLogOptions EventLog { get; set; } = new();
}

/// <summary>The level keys of the <c>Serilog:MinimumLevel</c> section.</summary>
public class MinimumLevelOptions
{
    /// <summary>
    /// The lowest level logged for sources without an <c>Override</c>: Verbose, Debug, Information, Warning, Error or
    /// Fatal. Applies live. Leave empty for the host's built-in levels (Information; Microsoft and the request
    /// diagnostics at Warning).
    /// </summary>
    [AllowedSettingValues("Verbose", "Debug", "Information", "Warning", "Error", "Fatal")]
    public string? Default { get; set; }
}

/// <summary>The keys of the <c>Serilog:EventLog</c> section.</summary>
[RestartRequired]
public class EventLogOptions
{
    /// <summary>Write Warning and up to the Windows Application event log. Windows only; off elsewhere whatever this says.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The event source, which must already be registered; empty uses ".NET Runtime", which exists on every Windows.</summary>
    public string? Source { get; set; }
}
