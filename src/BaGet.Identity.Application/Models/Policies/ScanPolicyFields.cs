using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>
/// The scanning policy fields as the form posts them: a severity is the text of a number 0 (low) to 3 (critical), or empty for
/// "off". Shared by the repository and the project policy pages.
/// </summary>
public sealed class ScanPolicyFields
{
    /// <summary>Warn from this severity.</summary>
    public string? Warn { get; set; }

    /// <summary>Refuse downloads from this severity.</summary>
    public string? BlockDownload { get; set; }

    /// <summary>Refuse deploys from this severity.</summary>
    public string? BlockDeploy { get; set; }

    /// <summary>Refuse downloads of versions that were not scanned.</summary>
    public bool BlockUnscanned { get; set; }

    /// <summary>The fields showing <paramref name="policy"/> (empty ones for none).</summary>
    public static ScanPolicyFields From(ScanPolicy? policy) => new()
    {
        Warn = policy?.WarnSeverity?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BlockDownload = policy?.BlockDownloadSeverity?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BlockDeploy = policy?.BlockDeploySeverity?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BlockUnscanned = policy?.BlockUnscanned ?? false,
    };

    /// <summary>The values the fields stand for.</summary>
    /// <exception cref="AccessModelException">A severity is not 0 to 3 or empty.</exception>
    public ScanPolicyValues ToValues()
    {
        var values = new ScanPolicyValues(Parse(Warn, "warn"), Parse(BlockDownload, "block download"), Parse(BlockDeploy, "block deploy"), BlockUnscanned);

        return values.Validate() is { } problem ? throw new AccessModelException(problem) : values;
    }

    private static int? Parse(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new AccessModelException($"The {label} severity is not valid.");
        }

        return value;
    }
}
