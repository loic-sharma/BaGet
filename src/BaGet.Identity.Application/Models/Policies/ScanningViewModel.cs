using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the vulnerability scanning overview (<c>/admin/scanning</c>) shows.</summary>
public sealed class ScanningViewModel
{
    /// <summary>
    /// The scanning options in force, for the view. Not serialised: the options class holds the scanner's registry credential; the JSON carries
    /// <see cref="Settings"/>, the values the screen shows.
    /// </summary>
    [JsonIgnore]
    public ScanningOptions Options { get; internal set; } = new();

    /// <summary>The scanning options the screen shows (no credential).</summary>
    public ScanningSettingsView Settings { get; internal set; } = new(false, true, string.Empty, null, TimeSpan.Zero);

    /// <summary>Whether Trivy is configured (container images are scanned).</summary>
    public bool TrivyConfigured { get; internal set; }

    /// <summary>The repositories that can hold artifacts (virtual ones hold none).</summary>
    public IReadOnlyList<ScanningRepositoryRow> Repositories { get; internal set; } = Array.Empty<ScanningRepositoryRow>();

    /// <summary>The projects.</summary>
    public IReadOnlyList<ScanningProjectRow> Projects { get; internal set; } = Array.Empty<ScanningProjectRow>();
}
