using System;
using System.Linq;

namespace BaGet.Core;

/// <summary>
/// One <c>Serilog:MinimumLevel:Override:{Source}</c> entry: the lowest level logged for a source (a namespace or type
/// name prefix such as <c>Microsoft.AspNetCore</c>).
/// </summary>
/// <param name="Source">The namespace or type name prefix.</param>
/// <param name="Level">Verbose, Debug, Information, Warning, Error or Fatal.</param>
/// <param name="InOverridesFile">True when the Settings overrides file holds it, so it can be removed from here; false when only a configuration file or the environment sets it.</param>
public sealed record LogLevelOverride(string Source, string Level, bool InOverridesFile);

/// <summary>
/// The rules for a per-source log level, shared by the Settings page and <see cref="SettingsService"/> (the one place that validates them).
/// </summary>
public static class LogLevelOverrides
{
    /// <summary>The configuration key whose children are the overrides.</summary>
    public const string SectionKey = "Serilog:MinimumLevel:Override";

    /// <summary>The longest source name accepted.</summary>
    public const int MaxSourceLength = 200;

    /// <summary>The level names Serilog reads, in order of severity.</summary>
    public static readonly string[] Levels = { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };

    /// <summary>The configuration key of <paramref name="source"/>.</summary>
    public static string KeyOf(string source) => SectionKey + ":" + source;

    /// <summary>
    /// The canonical spelling of <paramref name="level"/> (case-insensitive), or null when it is not a Serilog level.
    /// </summary>
    public static string? NormalizeLevel(string? level)
        => Levels.FirstOrDefault(l => string.Equals(l, level?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why <paramref name="source"/> cannot name a source, or null. Letters, digits, <c>.</c>, <c>_</c>, <c>-</c>,
    /// <c>`</c> and <c>+</c> only: no <c>:</c> (the configuration separator), no spaces.
    /// </summary>
    public static string? ValidateSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Name the source: a namespace or type name prefix such as Microsoft.AspNetCore.";
        if (source.Length > MaxSourceLength) return $"The source is longer than {MaxSourceLength} characters.";
        if (source.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '`' or '+')))
        {
            return "A source holds letters, digits and . _ - ` + only.";
        }

        return null;
    }
}
