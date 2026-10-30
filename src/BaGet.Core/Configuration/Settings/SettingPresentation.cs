using System;
using System.Collections.Generic;
using System.Text.Json;

namespace BaGet.Core;

/// <summary>
/// One entry of a drop-down setting.
/// </summary>
/// <param name="Value">The value posted back.</param>
/// <param name="Label">The text shown.</param>
/// <param name="Selected">The entry is the current value.</param>
public sealed record SettingChoice(string Value, string Label, bool Selected);

/// <summary>
/// What the Settings page shows for a setting beyond its raw value: drop-down entries and
/// examples. Kept out of the Razor view so that it is unit tested.
/// </summary>
public static class SettingPresentation
{
    /// <summary>The mask shown in place of a stored secret.</summary>
    public const string Mask = "••••";

    /// <summary>
    /// The entries of a drop-down: the allowed names, with the one equal to
    /// <paramref name="current"/> (case-insensitive) selected. A current value that is empty or not
    /// in the list is added, selected and labelled, so that saving the section never changes it.
    /// </summary>
    public static IReadOnlyList<SettingChoice> Choices(SettingDescriptor descriptor, string? current)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

        var list = new List<SettingChoice>();
        var matched = false;
        foreach (var value in descriptor.EnumValues)
        {
            var selected = !matched && string.Equals(value, current, StringComparison.OrdinalIgnoreCase);
            matched |= selected;
            list.Add(new SettingChoice(value, value, selected));
        }

        if (!matched)
        {
            var text = current ?? string.Empty;
            list.Add(new SettingChoice(text, text.Length == 0 ? "(not set)" : text + " (current, not in the list)", true));
        }

        return list;
    }

    /// <summary>
    /// The example of <paramref name="descriptor"/> for <paramref name="dependsValue"/> (case-insensitive), or null.
    /// </summary>
    public static string? ExampleFor(SettingDescriptor descriptor, string? dependsValue)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (descriptor.Examples == null || dependsValue == null) return null;

        foreach (var pair in descriptor.Examples)
        {
            if (string.Equals(pair.Key, dependsValue, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }

        return null;
    }

    /// <summary>
    /// The current value of the setting that selects <paramref name="descriptor"/>'s example, read from the
    /// settings of the same form, or null.
    /// </summary>
    public static string? DependsValue(SettingDescriptor descriptor, IEnumerable<SettingState> siblings)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (descriptor.ExampleDependsOnKey == null || siblings == null) return null;

        foreach (var sibling in siblings)
        {
            if (string.Equals(sibling.Descriptor.Key, descriptor.ExampleDependsOnKey, StringComparison.OrdinalIgnoreCase))
            {
                return sibling.EffectiveValue;
            }
        }

        return null;
    }

    /// <summary>
    /// The placeholder of a sensitive text input: "•••• unchanged" when a value is stored, otherwise the example
    /// ("e.g. ...") when there is one, otherwise "not set". The example is never the input's value.
    /// </summary>
    public static string Placeholder(SettingState state, string? dependsValue)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (state.HasValue) return Mask + " unchanged";

        var example = ExampleFor(state.Descriptor, dependsValue);
        return example == null ? "not set" : "e.g. " + example;
    }

    /// <summary>
    /// The value of the <c>data-examples</c> attribute: a JSON object of the examples keyed by the
    /// lower-cased dependsOn value (any characters, so no value can collide with another data attribute).
    /// The page script lower-cases the selected value to look it up. Encode it as an attribute value.
    /// </summary>
    public static string ExamplesJson(IReadOnlyDictionary<string, string> examples)
    {
        if (examples == null) throw new ArgumentNullException(nameof(examples));

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in examples) map[(pair.Key ?? string.Empty).ToLowerInvariant()] = pair.Value;

        return JsonSerializer.Serialize(map);
    }
}
