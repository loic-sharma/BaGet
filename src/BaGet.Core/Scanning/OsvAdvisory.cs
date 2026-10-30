using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// One vulnerability record in the OSV schema (<c>https://ossf.github.io/osv-schema/</c>), reduced to what scanning uses: the id, a
/// summary, a severity, a link, and the packages and versions it affects. Parsed from an OSV.dev answer or from a file of an
/// offline copy; the same matching decides whether it applies to a component.
/// </summary>
public sealed class OsvAdvisory
{
    private OsvAdvisory(string id, string? summary, int severity, string url, IReadOnlyList<OsvAffected> affected)
    {
        Id = id;
        Summary = summary;
        Severity = severity;
        Url = url;
        Affected = affected;
    }

    /// <summary>The identifier (<c>GHSA-…</c>, <c>CVE-…</c>, <c>PYSEC-…</c>).</summary>
    public string Id { get; }

    /// <summary>One line describing it, or null.</summary>
    public string? Summary { get; }

    /// <summary>0 low … 3 critical; <see cref="ScanSeverities.Unrated"/> when the record carries none.</summary>
    public int Severity { get; }

    /// <summary>An absolute http(s) link to the advisory (its OSV page when the record names no better).</summary>
    public string Url { get; }

    /// <summary>The package entries of the record.</summary>
    public IReadOnlyList<OsvAffected> Affected { get; }

    /// <summary>Whether the record withdraws itself (it is then ignored).</summary>
    public bool Withdrawn { get; private set; }

    /// <summary>Reads one record; null when it has no id.</summary>
    public static OsvAdvisory? Parse(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object) return null;
        var id = Text(record, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var summary = Text(record, "summary");
        if (string.IsNullOrWhiteSpace(summary))
        {
            var details = Text(record, "details");
            if (!string.IsNullOrWhiteSpace(details))
            {
                var line = details.Split('\n')[0].Trim();
                summary = line.Length > 300 ? line.Substring(0, 300) : line;
            }
        }
        else if (summary.Length > 500)
        {
            summary = summary.Substring(0, 500);
        }

        var advisory = new OsvAdvisory(id, string.IsNullOrWhiteSpace(summary) ? null : summary, ReadSeverity(record), ReadUrl(record, id), ReadAffected(record))
        {
            Withdrawn = record.TryGetProperty("withdrawn", out var withdrawn) && withdrawn.ValueKind == JsonValueKind.String,
        };
        return advisory;
    }

    /// <summary>
    /// Whether the record applies to <paramref name="component"/>, and the first version that fixes it when the record says.
    /// </summary>
    public bool Affects(ScanComponent component, out string? fixedVersion)
    {
        fixedVersion = null;
        if (Withdrawn) return false;

        var affects = false;
        foreach (var entry in Affected)
        {
            if (!string.Equals(entry.Ecosystem, component.Ecosystem, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entry.Name, component.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Versions.Any(v => string.Equals(v, component.Version, StringComparison.OrdinalIgnoreCase)))
            {
                affects = true;
            }

            foreach (var range in entry.Ranges)
            {
                if (range.IsAffected(component.Ecosystem, component.Version, out var fix))
                {
                    affects = true;
                    if (fix != null && (fixedVersion == null || OsvVersions.Compare(component.Ecosystem, fix, fixedVersion) < 0)) fixedVersion = fix;
                }
            }
        }

        return affects;
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int ReadSeverity(JsonElement record)
    {
        // GitHub advisories (the bulk of npm, Maven and NuGet records) name the level in database_specific.severity.
        if (record.TryGetProperty("database_specific", out var database) && database.ValueKind == JsonValueKind.Object
            && ScanSeverities.TryParseName(Text(database, "severity"), out var named))
        {
            return named;
        }

        if (record.TryGetProperty("severity", out var severities) && severities.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in severities.EnumerateArray())
            {
                var score = ScanSeverities.CvssV3BaseScore(Text(entry, "score"));
                if (score != null) return ScanSeverities.FromScore(score.Value);
            }
        }

        if (record.TryGetProperty("affected", out var affected) && affected.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in affected.EnumerateArray())
            {
                if (entry.TryGetProperty("ecosystem_specific", out var specific) && specific.ValueKind == JsonValueKind.Object
                    && ScanSeverities.TryParseName(Text(specific, "severity"), out var legacy))
                {
                    return legacy;
                }
            }
        }

        return ScanSeverities.Unrated;
    }

    private static string ReadUrl(JsonElement record, string id)
    {
        string? web = null;
        if (record.TryGetProperty("references", out var references) && references.ValueKind == JsonValueKind.Array)
        {
            foreach (var reference in references.EnumerateArray())
            {
                var url = Text(reference, "url");
                if (!IsHttp(url)) continue;
                var type = Text(reference, "type");
                if (string.Equals(type, "ADVISORY", StringComparison.OrdinalIgnoreCase)) return url!;
                if (web == null && string.Equals(type, "WEB", StringComparison.OrdinalIgnoreCase)) web = url;
            }
        }

        return web ?? "https://osv.dev/vulnerability/" + Uri.EscapeDataString(id);
    }

    private static bool IsHttp(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && url!.Length <= 900;

    private static IReadOnlyList<OsvAffected> ReadAffected(JsonElement record)
    {
        var result = new List<OsvAffected>();
        if (!record.TryGetProperty("affected", out var affected) || affected.ValueKind != JsonValueKind.Array) return result;

        foreach (var entry in affected.EnumerateArray())
        {
            if (!entry.TryGetProperty("package", out var package) || package.ValueKind != JsonValueKind.Object) continue;
            var ecosystem = Text(package, "ecosystem");
            var name = Text(package, "name");
            if (ecosystem == null || name == null) continue;

            var versions = new List<string>();
            if (entry.TryGetProperty("versions", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                versions.AddRange(list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!));
            }

            var ranges = new List<OsvRange>();
            if (entry.TryGetProperty("ranges", out var rangeList) && rangeList.ValueKind == JsonValueKind.Array)
            {
                foreach (var range in rangeList.EnumerateArray())
                {
                    var type = Text(range, "type");
                    if (type != "SEMVER" && type != "ECOSYSTEM") continue;
                    if (!range.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) continue;

                    var parsed = new List<OsvEvent>();
                    foreach (var e in events.EnumerateArray())
                    {
                        if (e.TryGetProperty("introduced", out var introduced) && introduced.ValueKind == JsonValueKind.String)
                            parsed.Add(new OsvEvent(OsvEventKind.Introduced, introduced.GetString()!));
                        else if (e.TryGetProperty("fixed", out var fix) && fix.ValueKind == JsonValueKind.String)
                            parsed.Add(new OsvEvent(OsvEventKind.Fixed, fix.GetString()!));
                        else if (e.TryGetProperty("last_affected", out var last) && last.ValueKind == JsonValueKind.String)
                            parsed.Add(new OsvEvent(OsvEventKind.LastAffected, last.GetString()!));
                    }

                    ranges.Add(new OsvRange(parsed));
                }
            }

            result.Add(new OsvAffected(ecosystem, name, versions, ranges));
        }

        return result;
    }
}

/// <summary>One <c>affected</c> entry of an OSV record: a package and the versions it covers.</summary>
public sealed record OsvAffected(string Ecosystem, string Name, IReadOnlyList<string> Versions, IReadOnlyList<OsvRange> Ranges);

/// <summary>What an <see cref="OsvEvent"/> says about its version.</summary>
public enum OsvEventKind
{
    /// <summary>Versions from here on are affected (<c>0</c> = from the start).</summary>
    Introduced,

    /// <summary>Versions from here on are not affected.</summary>
    Fixed,

    /// <summary>Versions after this one are not affected; this one is.</summary>
    LastAffected,
}

/// <summary>One event of a range.</summary>
public sealed record OsvEvent(OsvEventKind Kind, string Version);

/// <summary>A SEMVER or ECOSYSTEM range of an OSV record: ordered events that switch "affected" on and off.</summary>
public sealed class OsvRange
{
    private readonly IReadOnlyList<OsvEvent> _events;

    public OsvRange(IReadOnlyList<OsvEvent> events)
    {
        _events = events;
    }

    /// <summary>The events as written.</summary>
    public IReadOnlyList<OsvEvent> Events => _events;

    /// <summary>
    /// Whether <paramref name="version"/> is inside the range (OSV's evaluation: walk the events in version order, every
    /// <c>introduced</c> at or below the version switches it on, a <c>fixed</c> at or below switches it off, a
    /// <c>last_affected</c> below it switches it off), and the <c>fixed</c> version that ends the affected stretch.
    /// </summary>
    public bool IsAffected(string ecosystem, string version, out string? fixedVersion)
    {
        fixedVersion = null;
        var affected = false;
        var ordered = _events
            .OrderBy(e => e.Version == "0" ? 0 : 1)
            .ThenBy(e => e.Version, Comparer<string>.Create((a, b) => a == "0" ? (b == "0" ? 0 : -1) : b == "0" ? 1 : OsvVersions.Compare(ecosystem, a, b)))
            .ThenBy(e => e.Kind == OsvEventKind.Introduced ? 0 : 1);

        foreach (var e in ordered)
        {
            var cmp = e.Version == "0" ? -1 : OsvVersions.Compare(ecosystem, e.Version, version);
            switch (e.Kind)
            {
                case OsvEventKind.Introduced:
                    if (cmp <= 0) affected = true;
                    break;
                case OsvEventKind.Fixed:
                    if (cmp <= 0) affected = false;
                    else if (affected) fixedVersion ??= e.Version;
                    break;
                case OsvEventKind.LastAffected:
                    if (cmp < 0) affected = false;
                    break;
            }
        }

        if (!affected) fixedVersion = null;
        return affected;
    }
}

/// <summary>Version order for the OSV ecosystems BaGet scans (NuGet, npm, Maven).</summary>
public static class OsvVersions
{
    /// <summary>
    /// Compares two versions: semantic versions as such (NuGet and npm versions parse as <see cref="NuGetVersion"/>), anything else
    /// (most Maven versions) by its numeric and textual parts, with <c>alpha &lt; beta &lt; milestone &lt; rc &lt; snapshot &lt;
    /// release &lt; sp &lt; any other qualifier</c> (lexically) as in Maven.
    /// </summary>
    public static int Compare(string ecosystem, string left, string right)
    {
        if (string.Equals(ecosystem, "Maven", StringComparison.OrdinalIgnoreCase)) return CompareLoose(left, right);
        if (NuGetVersion.TryParse(left, out var a) && NuGetVersion.TryParse(right, out var b)) return a.CompareTo(b);
        return CompareLoose(left, right);
    }

    private static int CompareLoose(string left, string right)
    {
        var a = Tokenize(left);
        var b = Tokenize(right);
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : null;
            var y = i < b.Count ? b[i] : null;
            var cmp = CompareToken(x, y);
            if (cmp != 0) return cmp;
        }

        return 0;
    }

    private static List<string> Tokenize(string version)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool? digits = null;
        foreach (var ch in version)
        {
            if (ch == '.' || ch == '-' || ch == '_' || ch == '+')
            {
                Flush();
                continue;
            }

            var isDigit = char.IsAsciiDigit(ch);
            if (digits != null && digits != isDigit) Flush();
            digits = isDigit;
            current.Append(char.ToLowerInvariant(ch));
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Length > 0) tokens.Add(current.ToString());
            current.Clear();
            digits = null;
        }
    }

    private static int CompareToken(string? x, string? y)
    {
        var xn = x != null && x.All(char.IsAsciiDigit);
        var yn = y != null && y.All(char.IsAsciiDigit);
        if ((x == null || xn) && (y == null || yn))
        {
            return Number(x).CompareTo(Number(y));
        }

        // A number beats a qualifier of the same position (1.0.1 > 1.0-rc), a missing token ranks as the release itself.
        var byRank = Rank(x).CompareTo(Rank(y));

        // Maven puts an unknown qualifier (jre, android...) after the release and orders two of them lexically.
        return byRank != 0 || Rank(x) != UnknownRank ? byRank : string.CompareOrdinal(x, y);
    }

    private const int UnknownRank = 120;

    private static System.Numerics.BigInteger Number(string? value)
        => string.IsNullOrEmpty(value) ? System.Numerics.BigInteger.Zero : System.Numerics.BigInteger.Parse(value);

    private static int Rank(string? token)
    {
        if (token == null || token.Length == 0) return 100;
        if (token.All(char.IsAsciiDigit)) return 200;
        return token switch
        {
            "alpha" or "a" => 10,
            "beta" or "b" => 20,
            "milestone" or "m" => 30,
            "rc" or "cr" => 40,
            "snapshot" => 50,
            "ga" or "final" or "release" => 100,
            "sp" => 110,
            _ => UnknownRank,
        };
    }
}
