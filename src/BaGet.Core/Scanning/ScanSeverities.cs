using System;
using System.Collections.Generic;
using System.Globalization;

namespace BaGet.Core;

/// <summary>
/// Severities as the scanner stores them: the numbers of NuGet's vulnerability resource (0 low, 1 moderate, 2 high, 3 critical),
/// with -1 for "none". Advisory databases name them differently (GitHub: LOW, MODERATE, HIGH, CRITICAL; Trivy: LOW, MEDIUM, HIGH,
/// CRITICAL, UNKNOWN) or give only a CVSS vector; this class maps all of them onto the four.
/// </summary>
public static class ScanSeverities
{
    /// <summary>"No finding".</summary>
    public const int None = -1;

    /// <summary>The severity of a finding the source does not rate; treated as moderate so it is neither hidden nor alarming.</summary>
    public const int Unrated = 1;

    /// <summary>The display name of a severity (<c>Low</c>… <c>Critical</c>), or <c>None</c>.</summary>
    public static string Name(int severity)
        => severity >= 0 && severity < PackageVulnerabilityRules.SeverityNames.Count ? PackageVulnerabilityRules.SeverityNames[severity] : "None";

    /// <summary>The severity named by a database (case-insensitive), or false when it is not one of the usual words.</summary>
    public static bool TryParseName(string? name, out int severity)
    {
        switch (name?.Trim().ToUpperInvariant())
        {
            case "LOW": severity = 0; return true;
            case "MODERATE":
            case "MEDIUM": severity = 1; return true;
            case "HIGH": severity = 2; return true;
            case "CRITICAL": severity = 3; return true;
            default: severity = Unrated; return false;
        }
    }

    /// <summary>The CVSS qualitative rating of a base score: below 4 low, below 7 moderate, below 9 high, else critical.</summary>
    public static int FromScore(double score) => score < 4.0 ? 0 : score < 7.0 ? 1 : score < 9.0 ? 2 : 3;

    /// <summary>
    /// The CVSS v3.x base score of a vector (<c>CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H</c>), or null when it is not a valid
    /// v3 vector. Version 4 vectors are not computed (they carry no number); the caller falls back to <see cref="Unrated"/>.
    /// </summary>
    public static double? CvssV3BaseScore(string? vector)
    {
        if (string.IsNullOrWhiteSpace(vector) || !vector.StartsWith("CVSS:3", StringComparison.Ordinal)) return null;

        var metrics = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in vector.Split('/'))
        {
            var colon = part.IndexOf(':');
            if (colon > 0) metrics[part.Substring(0, colon)] = part.Substring(colon + 1);
        }

        if (!metrics.TryGetValue("AV", out var av) || !metrics.TryGetValue("AC", out var ac) || !metrics.TryGetValue("PR", out var pr)
            || !metrics.TryGetValue("UI", out var ui) || !metrics.TryGetValue("S", out var scope)
            || !metrics.TryGetValue("C", out var c) || !metrics.TryGetValue("I", out var i) || !metrics.TryGetValue("A", out var a))
        {
            return null;
        }

        var changed = scope == "C";
        if (scope != "C" && scope != "U") return null;

        double? avW = av switch { "N" => 0.85, "A" => 0.62, "L" => 0.55, "P" => 0.2, _ => null };
        double? acW = ac switch { "L" => 0.77, "H" => 0.44, _ => null };
        double? prW = pr switch { "N" => 0.85, "L" => changed ? 0.68 : 0.62, "H" => changed ? 0.5 : 0.27, _ => null };
        double? uiW = ui switch { "N" => 0.85, "R" => 0.62, _ => null };
        double? cW = Cia(c), iW = Cia(i), aW = Cia(a);
        if (avW == null || acW == null || prW == null || uiW == null || cW == null || iW == null || aW == null) return null;

        var iss = 1 - (1 - cW.Value) * (1 - iW.Value) * (1 - aW.Value);
        var impact = changed ? 7.52 * (iss - 0.029) - 3.25 * Math.Pow(iss - 0.02, 15) : 6.42 * iss;
        var exploitability = 8.22 * avW.Value * acW.Value * prW.Value * uiW.Value;
        if (impact <= 0) return 0;

        var raw = changed ? Math.Min(1.08 * (impact + exploitability), 10) : Math.Min(impact + exploitability, 10);
        return Math.Ceiling(raw * 10 - 1e-9) / 10;

        static double? Cia(string value) => value switch { "H" => 0.56, "L" => 0.22, "N" => 0.0, _ => null };
    }

    /// <summary>Formats a version number for display in a reason (invariant culture).</summary>
    internal static string Format(int count) => count.ToString(CultureInfo.InvariantCulture);
}
