using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using BaGet.Core;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Settings;

/// <summary>What the server settings screen (<c>/admin/settings</c>) shows: every registered setting by section and the state of the last save.</summary>
public sealed class AdminSettingsViewModel : ConfirmationModel, IFlashMessage
{
    private IReadOnlyDictionary<string, string> _ids = new Dictionary<string, string>();

    /// <summary>The settings grouped by section; the top-level keys come first, under the empty key. Not serialised: <see cref="SectionList"/> is the same with the keys.</summary>
    [JsonIgnore]
    public IReadOnlyList<IGrouping<string, SettingState>> Sections { get; internal set; } = Array.Empty<IGrouping<string, SettingState>>();

    /// <summary>The same sections as <see cref="Sections"/>, each with its key and DOM id, as the JSON carries them.</summary>
    public IReadOnlyList<SettingsSection> SectionList
        => Sections.Select(group => new SettingsSection(group.Key, SectionId(group.Key), group.ToList())).ToList();

    /// <summary>The file the settings are saved to.</summary>
    public string? OverridesPath { get; internal set; }

    /// <summary>The keys changed since startup that only apply after a restart.</summary>
    public IReadOnlyList<string> PendingRestart { get; internal set; } = Array.Empty<string>();

    /// <summary>Why the last save was refused.</summary>
    public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();

    /// <summary>The section the <see cref="Errors"/> belong to.</summary>
    public string? ErrorSection { get; internal set; }

    /// <summary>The per-source log levels (<c>Serilog:MinimumLevel:Override</c>) shown under the Serilog section.</summary>
    public IReadOnlyList<LogLevelOverride> LogLevels { get; internal set; } = Array.Empty<LogLevelOverride>();

    /// <summary>The level names offered for a source.</summary>
    public IReadOnlyList<string> LogLevelNames => LogLevelOverrides.Levels;

    /// <summary>The named OpenID Connect providers (<c>Accounts:OpenIdConnectProviders</c>) shown in the "Sign-in providers" panel of the Accounts section. No secret is carried.</summary>
    public IReadOnlyList<SignInProviderView> SignInProviders { get; internal set; } = Array.Empty<SignInProviderView>();

    /// <summary>Why the overrides file could not be read, or null.</summary>
    public string? LoadError { get; internal set; }

    /// <summary>The state of each telemetry signal for the Telemetry section, or null when the host exports no telemetry.</summary>
    public IReadOnlyList<TelemetrySignalReport>? TelemetrySignals { get; internal set; }

    /// <summary>
    /// True when this host exports telemetry (registers <see cref="ITelemetryDiagnostics"/>). When false the Telemetry
    /// section stays editable (the settings file may be shared with a host that does export) but a note says it has no effect here.
    /// </summary>
    public bool HostExportsTelemetry => TelemetrySignals != null;

    /// <summary>The outcome of the last "Test connection", or null. Shown in the Telemetry section; never saved.</summary>
    public TelemetryTestOutcome? TelemetryTest { get; internal set; }

    /// <summary>
    /// Whether the running database is relational, so a change of database can copy the data (the "Copy all data" option of the
    /// Database section).
    /// </summary>
    public bool CanCopyDatabase { get; internal set; }

    /// <summary>The <c>Database:Type</c> values a copy can target; the option is hidden for any other type.</summary>
    public IReadOnlyList<string> CopyTargetTypes { get; internal set; } = Array.Empty<string>();

    /// <summary>The state of the database copy, if one was requested since startup.</summary>
    public DatabaseCopyStatus CopyStatus { get; internal set; } = null!;

    /// <summary>
    /// The DOM id (slug) of the section shown; every other section is rendered <c>hidden</c>. Chosen by the workflow: the failed
    /// section, else the Telemetry section after "Test connection", else the <c>?section=</c> value, else the first section. The URL
    /// fragment is only known to the browser, so the page's script applies it on load unless <see cref="SelectionLocked"/>.
    /// </summary>
    public string SelectedId { get; internal set; } = string.Empty;

    /// <summary>True when the server picked the section for a reason the URL fragment must not override (a failed save, a connection test).</summary>
    public bool SelectionLocked { get; internal set; }

    /// <inheritdoc />
    public string? Message { get; set; }

    internal void SetIds(IReadOnlyDictionary<string, string> ids) => _ids = ids;

    internal IEnumerable<string> Ids => _ids.Values;

    internal bool TryGetId(string key, out string id) => _ids.TryGetValue(key, out id!);

    /// <summary>
    /// The stable DOM id of a section: <c>general</c> for the empty key, else the key with every character other than letters,
    /// digits, <c>-</c> and <c>_</c> replaced by <c>-</c> (made unique, ignoring case, if two keys collapse to the same id). Used by
    /// the nav, the form, the fragment and the redirect after a save.
    /// </summary>
    public string SectionId(string key) => _ids.TryGetValue(key ?? string.Empty, out var id) ? id : BuildId(key ?? string.Empty);

    internal static string BuildId(string key)
    {
        if (key.Length == 0) return "general";

        var chars = key.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray();
        return new string(chars);
    }
}
