using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Primitives;

namespace BaGet.Core;

/// <summary>
/// Turns a submitted Settings form into the changes <see cref="SettingsService.SaveAsync"/> takes.
/// </summary>
/// <remarks>
/// Form fields, per setting key:
/// <list type="bullet">
/// <item><c>v:&lt;key&gt;</c>, the value. A boolean is a checkbox "true" plus a hidden "false" (true
/// wins). Numbers and durations use the invariant culture. An enum must be one of the allowed
/// names. A list is one entry per line, blank lines dropped.</item>
/// <item><c>reset:&lt;key&gt;</c>, removes the saved override.</item>
/// <item><c>clear:&lt;key&gt;</c>, sensitive settings only: saves an empty value. A blank
/// <c>v:</c> for a sensitive setting means unchanged.</item>
/// </list>
/// Only values that differ from the effective value are returned.
/// </remarks>
internal static class SettingsForm
{
    /// <summary>The field prefix of a value.</summary>
    public const string ValuePrefix = "v:";

    /// <summary>The field prefix of a reset to the default.</summary>
    public const string ResetPrefix = "reset:";

    /// <summary>The field prefix of an explicit clear of a sensitive value.</summary>
    public const string ClearPrefix = "clear:";

    /// <summary>
    /// Parses the form against the settings it was rendered from.
    /// </summary>
    /// <param name="form">The submitted fields (an ASP.NET form collection enumerates as this).</param>
    /// <param name="states">The settings of the submitted section.</param>
    /// <returns>The changes, and one "key: reason" per field that failed to parse.</returns>
    public static (IReadOnlyDictionary<SettingDescriptor, object> Changes, IReadOnlyList<string> Errors) Parse(
        IEnumerable<KeyValuePair<string, StringValues>> form,
        IEnumerable<SettingState> states)
    {
        if (form == null) throw new ArgumentNullException(nameof(form));
        if (states == null) throw new ArgumentNullException(nameof(states));

        var fields = new Dictionary<string, StringValues>(StringComparer.Ordinal);
        foreach (var pair in form) fields[pair.Key] = pair.Value;

        var changes = new Dictionary<SettingDescriptor, object>();
        var errors = new List<string>();

        foreach (var state in states)
        {
            var descriptor = state.Descriptor;
            if (descriptor.Kind == SettingKind.ReadOnly) continue;

            var key = descriptor.Key;

            if (fields.ContainsKey(ResetPrefix + key))
            {
                #pragma warning disable CS8625
                if (state.IsOverridden) changes[descriptor] = null;
                #pragma warning restore CS8625
                continue;
            }

            if (descriptor.IsSensitive)
            {
                if (fields.ContainsKey(ClearPrefix + key))
                {
                    if (state.HasValue || state.IsOverridden) changes[descriptor] = string.Empty;
                }
                else if (fields.TryGetValue(ValuePrefix + key, out var secret) && !string.IsNullOrEmpty(secret.ToString()))
                {
                    changes[descriptor] = secret.ToString();
                }

                continue;
            }

            if (!fields.TryGetValue(ValuePrefix + key, out var raw)) continue;

            // A drop-down that listed a current value outside its list posts it back: not a change.
            if (descriptor.Kind == SettingKind.Enum
                && string.Equals(RawText(raw), state.EffectiveValue ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var parsed = ParseValue(descriptor, raw);
                if (parsed.HasValue && Normalize(descriptor, parsed.Value) != Normalize(descriptor, state.EffectiveValue))
                {
                    changes[descriptor] = parsed.Value;
                }
            }
            catch (FormatException e)
            {
                errors.Add($"{key}: {e.Message}");
            }
        }

        return (changes, errors);
    }

    private static string RawText(StringValues raw)
        => (raw.Count == 0 ? string.Empty : raw[raw.Count - 1] ?? string.Empty).Trim();

    private static (bool HasValue, object Value) ParseValue(SettingDescriptor descriptor, StringValues raw)
    {
        // A checkbox posts "true" plus the hidden "false": true wins.
        if (descriptor.Kind == SettingKind.Boolean)
        {
            return (true, raw.Any(v => string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase)));
        }

        var text = (raw.Count == 0 ? string.Empty : raw[raw.Count - 1] ?? string.Empty).Trim();

        switch (descriptor.Kind)
        {
            case SettingKind.StringList:
                return (true, text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToArray());

            case SettingKind.String:
                return (true, text);
        }

        // A blank number, duration or name is left unchanged: "reset" is how to go back to the default.
        #pragma warning disable CS8619
        if (text.Length == 0) return (false, null);
        #pragma warning restore CS8619

        switch (descriptor.Kind)
        {
            case SettingKind.Integer:
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    || !FitsInteger(descriptor.ClrType, whole))
                {
                    throw new FormatException($"\"{text}\" is not a valid whole number.");
                }

                return (true, whole);

            case SettingKind.Number:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || double.IsNaN(number)
                    || double.IsInfinity(number))
                {
                    throw new FormatException($"\"{text}\" is not a valid number.");
                }

                return (true, number);

            case SettingKind.TimeSpan:
                if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span))
                {
                    throw new FormatException($"\"{text}\" is not a valid duration (use hh:mm:ss).");
                }

                return (true, span);

            case SettingKind.Enum:
                var name = descriptor.EnumValues.FirstOrDefault(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase));
                if (name == null)
                {
                    throw new FormatException($"\"{text}\" is not one of: {string.Join(", ", descriptor.EnumValues)}.");
                }

                return (true, name);

            default:
                throw new FormatException("This setting cannot be edited here.");
        }
    }

    private static bool FitsInteger(Type type, long value)
    {
        try
        {
            Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string? Normalize(SettingDescriptor descriptor, object value)
        => value switch
        {
            null => string.Empty,
            bool flag => flag ? "true" : "false",
            string text => Normalize(descriptor, text),
            string[] list => string.Join("\n", list),
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

    private static string Normalize(SettingDescriptor descriptor, string? text)
    {
        text ??= string.Empty;

        switch (descriptor.Kind)
        {
            case SettingKind.Boolean:
                return bool.TryParse(text, out var flag) && flag ? "true" : "false";
            case SettingKind.Integer:
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    ? whole.ToString(CultureInfo.InvariantCulture)
                    : text;
            case SettingKind.Number:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : text;
            case SettingKind.TimeSpan:
                return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span)
                    ? span.ToString("c", CultureInfo.InvariantCulture)
                    : text;
            case SettingKind.Enum:
                return descriptor.EnumValues.FirstOrDefault(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase)) ?? text;
            default:
                return text;
        }
    }
}
