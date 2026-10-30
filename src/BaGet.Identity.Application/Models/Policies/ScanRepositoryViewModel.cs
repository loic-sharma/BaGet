using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BaGet.Core;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the scanning dashboard of one repository (<c>/admin/scanning/{name}</c>) shows.</summary>
public sealed class ScanRepositoryViewModel : PolicyViewModel
{
    /// <summary>Versions listed per page.</summary>
    public const int PageSize = 50;

    /// <summary>The repository's name (route).</summary>
    public string? Name { get; set; }

    /// <summary>The lowest severity listed (0 low ... 3 critical).</summary>
    public int MinSeverity { get; set; }

    /// <summary>The page of the list, from 1.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>The repository.</summary>
    public RepositoryInfo? Repository { get; internal set; }

    /// <summary>The counts.</summary>
    public ScanSummary? Summary { get; internal set; }

    /// <summary>The policy in force and where it comes from.</summary>
    public EffectiveScanPolicy Effective { get; internal set; } = EffectiveScanPolicy.None;

    /// <summary>The policy stored for this repository itself, or null.</summary>
    public ScanPolicy? Own { get; internal set; }

    /// <summary>The versions with findings on this page.</summary>
    public IReadOnlyList<ScanResult> Findings { get; internal set; } = Array.Empty<ScanResult>();

    /// <summary>Whether there is another page.</summary>
    public bool HasMore { get; internal set; }

    /// <summary>The last run of the repository scan, or null.</summary>
    public AdminJobStatus? Job { get; internal set; }

    /// <summary>The fields of the policy editor (for the view; the JSON carries <see cref="Own"/>).</summary>
    [JsonIgnore]
    public ScanPolicyFormView PolicyForm => new(ScanPolicyFields.From(Own), Confirmation("scan-policy-password"), "scan-policy");

    /// <summary>The package screen of each finding's version (by <see cref="ScanResult.Key"/>), when its format has one.</summary>
    public IReadOnlyDictionary<int, ScreenLink> PackageLinks { get; internal set; } = new Dictionary<int, ScreenLink>();

    /// <summary>Turns a <see cref="ScreenLink"/> into an address; set by the view controller (the use case knows no routes).</summary>
    [JsonIgnore]
    public Func<ScreenLink, string?>? LinkResolver { get; set; }

    /// <summary>The page of a version in its format (the repository's package page), for the findings list.</summary>
    public string? PackageUrl(ScanResult result)
        => PackageLinks.TryGetValue(result.Key, out var link) ? LinkResolver?.Invoke(link) : null;
}
