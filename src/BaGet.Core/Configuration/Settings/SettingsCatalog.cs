using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace BaGet.Core;

/// <summary>
/// The editable type of a setting.
/// </summary>
public enum SettingKind
{
    /// <summary>A true or false value.</summary>
    Boolean,

    /// <summary>A whole number.</summary>
    Integer,

    /// <summary>A number with a fraction.</summary>
    Number,

    /// <summary>Free text.</summary>
    String,

    /// <summary>
    /// One of a fixed set of names, see <see cref="SettingDescriptor.EnumValues"/>: an enum, or a
    /// string property carrying <see cref="AllowedSettingValuesAttribute"/>.
    /// </summary>
    Enum,

    /// <summary>A duration such as "00:10:00".</summary>
    TimeSpan,

    /// <summary>A list of text entries.</summary>
    StringList,

    /// <summary>Shown but not editable here (a dictionary or other unsupported type).</summary>
    ReadOnly,
}

/// <summary>
/// One configuration key the Settings page can show.
/// </summary>
/// <param name="Key">The full key, such as "Email:Smtp:Host".</param>
/// <param name="Section">The first segment of the key, or an empty string for a top-level key.</param>
/// <param name="Group">The options type's name without "Options", such as "AzureBlobStorage".</param>
/// <param name="Kind">How the value is edited.</param>
/// <param name="ClrType">The property's type, without <see cref="Nullable{T}"/> (not serialised: a <see cref="Type"/> has no JSON form).</param>
/// <param name="EnumValues">The allowed names of an <see cref="SettingKind.Enum"/>, otherwise empty.</param>
/// <param name="IsSensitive">The value is a credential: masked and write-only.</param>
/// <param name="RequiresRestart">A change applies after a restart.</param>
/// <param name="Description">The property's XML documentation summary, or an empty string.</param>
/// <param name="ExampleDependsOnKey">
/// The full key of the setting that selects <paramref name="Examples"/> (see
/// <see cref="SettingExampleAttribute"/>), or null.
/// </param>
/// <param name="Examples">
/// Example values by the value of <paramref name="ExampleDependsOnKey"/> (case-insensitive), or null.
/// </param>
public sealed record SettingDescriptor(
    string Key,
    string Section,
    string Group,
    SettingKind Kind,
    [property: JsonIgnore] Type ClrType,
    string[] EnumValues,
    bool IsSensitive,
    bool RequiresRestart,
    string Description,
    string? ExampleDependsOnKey = null,
    IReadOnlyDictionary<string, string>? Examples = null);

/// <summary>
/// Every setting of every registered options type, found by reflection over the types
/// recorded as <see cref="SettingsSchemaRegistration"/>.
/// </summary>
public class SettingsCatalog
{
    private const string DocsBaseUrl = "https://pieroviano.github.io/BaGet/configuration/";
    private const int MaxDepth = 4;

    private static readonly Dictionary<string, string> DocsAnchors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Accounts"] = "accounts-package-owners-and-api-keys",
        ["Email"] = "email-optional",
        ["Site"] = "legal-pages",
        ["Feeds"] = "nuget-v2-feed",
        ["Validation"] = "validate-packages-before-serving-them",
        ["Serilog"] = "logging",
        ["Mirror"] = "enable-read-through-caching",
        ["Storage"] = "storage",
        ["Search"] = "search",
        ["Database"] = "database-configuration",
        ["Ha"] = "high-availability",
        ["IISServerOptions"] = "iis-server-options",
        ["PathBase"] = "listening-address",
        ["Urls"] = "listening-address",
        ["AllowedHosts"] = "restrict-the-host-names-baget-extension-serves",
        ["ApiKey"] = "require-an-api-key",
        ["AllowPackageOverwrites"] = "enable-package-overwrites",
        ["PackageDeletionBehavior"] = "enable-package-hard-deletions",
    };

    private readonly List<SettingDescriptor> _descriptors;

    /// <summary>
    /// Builds the catalog.
    /// </summary>
    /// <param name="registrations">The options types recorded by <c>AddBaGetOptions</c>.</param>
    public SettingsCatalog(IEnumerable<SettingsSchemaRegistration> registrations)
    {
        if (registrations == null) throw new ArgumentNullException(nameof(registrations));

        var byKey = new Dictionary<string, SettingDescriptor>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var registration in registrations)
        {
            Collect(
                registration.OptionsType,
                registration.Section,
                scalarsOnly: registration.Section == null,
                IsRestartOnly(registration.OptionsType),
                depth: 0,
                byKey,
                order);
        }

        _descriptors = order.Select(k => byKey[k]).ToList();
    }

    /// <summary>
    /// The settings, in registration order.
    /// </summary>
    public IReadOnlyList<SettingDescriptor> Descriptors => _descriptors;

    /// <summary>
    /// The settings of one section ("" for the top-level keys).
    /// </summary>
    public IEnumerable<SettingDescriptor> InSection(string? section)
        => _descriptors.Where(d => string.Equals(d.Section, section ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The setting with this key, or null.
    /// </summary>
    public SettingDescriptor? Find(string key)
        => _descriptors.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The documentation link for a section of the Settings page: the matching heading of
    /// the configuration page, or the top of that page if the section has none.
    /// </summary>
    public static string DocsAnchor(string section)
        => !string.IsNullOrEmpty(section) && DocsAnchors.TryGetValue(section, out var anchor)
            ? DocsBaseUrl + "#" + anchor
            : DocsBaseUrl;

    private static bool IsRestartOnly(Type type)
        // A type outside BaGet (such as the framework's IISServerOptions) is read when the
        // host is built, so unless it says otherwise a change needs a restart.
        => type.GetCustomAttribute<RestartRequiredAttribute>(inherit: true) != null
           || !(type.Assembly.GetName().Name ?? string.Empty).StartsWith("BaGet", StringComparison.Ordinal);

    private static void Collect(
        Type type,
        string? prefix,
        bool scalarsOnly,
        bool inheritedRestart,
        int depth,
        Dictionary<string, SettingDescriptor> byKey,
        List<string> order)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0
                || !property.CanRead
                || property.SetMethod == null
                || !property.SetMethod.IsPublic
                || property.GetCustomAttribute<HiddenSettingAttribute>() != null)
            {
                continue;
            }

            var key = string.IsNullOrEmpty(prefix) ? property.Name : prefix + ":" + property.Name;
            var restart = inheritedRestart
                          || property.GetCustomAttribute<RestartRequiredAttribute>() != null
                          || property.DeclaringType?.GetCustomAttribute<RestartRequiredAttribute>(inherit: true) != null;

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (IsComplex(propertyType))
            {
                // The root's nested objects are covered by their own registrations.
                if (scalarsOnly || depth >= MaxDepth) continue;

                Collect(propertyType, key, false, restart, depth + 1, byKey, order);
                continue;
            }

            var allowed = property.GetCustomAttribute<AllowedSettingValuesAttribute>()?.Values;
            var kind = allowed != null && propertyType == typeof(string) ? SettingKind.Enum : KindOf(propertyType);
            var examples = property.GetCustomAttributes<SettingExampleAttribute>().ToList();
            var descriptor = new SettingDescriptor(
                Key: key,
                Section: key.Contains(':') ? key.Substring(0, key.IndexOf(':')) : (scalarsOnly ? string.Empty : key),
                Group: scalarsOnly ? "BaGet" : GroupOf(property),
                Kind: kind,
                ClrType: propertyType,
                EnumValues: kind != SettingKind.Enum ? Array.Empty<string>() : allowed ?? Enum.GetNames(propertyType),
                IsSensitive: property.GetCustomAttribute<SensitiveSettingAttribute>() != null,
                RequiresRestart: restart,
                Description: XmlDocumentation.GetSummary(property),
                ExampleDependsOnKey: examples.Count == 0
                    ? null
                    : string.IsNullOrEmpty(prefix) ? examples[0].DependsOn : prefix + ":" + examples[0].DependsOn,
                Examples: ExamplesOf(property, key, examples));

            if (byKey.TryGetValue(key, out var existing))
            {
                // Two options types sharing a key (such as "Storage:Type") are one setting.
                // The choices and examples of whichever type declares them survive the merge.
                var choice = existing.Kind == SettingKind.Enum && existing.ClrType == typeof(string)
                             || descriptor.Kind != SettingKind.Enum || descriptor.ClrType != typeof(string)
                    ? existing
                    : descriptor;
                byKey[key] = existing with
                {
                    Kind = choice.Kind,
                    EnumValues = choice.EnumValues,
                    ExampleDependsOnKey = existing.ExampleDependsOnKey ?? descriptor.ExampleDependsOnKey,
                    Examples = existing.Examples ?? descriptor.Examples,
                    IsSensitive = existing.IsSensitive || descriptor.IsSensitive,
                    RequiresRestart = existing.RequiresRestart || descriptor.RequiresRestart,
                    Description = existing.Description.Length > 0 ? existing.Description : descriptor.Description,
                };
            }
            else
            {
                byKey[key] = descriptor;
                order.Add(key);
            }
        }
    }

    private static Dictionary<string, string>? ExamplesOf(
        PropertyInfo property,
        string key,
        List<SettingExampleAttribute> examples)
    {
        if (examples.Count == 0) return null;

        // The settings page selects an example by one other setting, so every example of a property
        // must depend on the same one and name each value once (case-insensitively).
        var dependsOn = examples[0].DependsOn;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var example in examples)
        {
            if (!string.Equals(example.DependsOn, dependsOn, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Setting '{key}' ({property.DeclaringType?.Name}.{property.Name}) has [SettingExample] attributes " +
                    $"that depend on different properties ('{dependsOn}' and '{example.DependsOn}'); only one is supported.");
            }

            if (!result.TryAdd(example.WhenValue, example.Example))
            {
                throw new InvalidOperationException(
                    $"Setting '{key}' ({property.DeclaringType?.Name}.{property.Name}) has more than one [SettingExample] " +
                    $"for the value '{example.WhenValue}' of '{dependsOn}' (values are compared ignoring case).");
            }
        }

        return result;
    }

    private static string GroupOf(PropertyInfo property)
    {
        var name = property.DeclaringType?.Name ?? string.Empty;
        return name.EndsWith("Options", StringComparison.Ordinal) && name.Length > "Options".Length
            ? name.Substring(0, name.Length - "Options".Length)
            : name;
    }

    private static bool IsComplex(Type type)
        => type.IsClass
           && type != typeof(string)
           && type != typeof(Uri)
           && !typeof(IEnumerable).IsAssignableFrom(type);

    private static SettingKind KindOf(Type type)
    {
        if (type == typeof(bool)) return SettingKind.Boolean;
        if (type == typeof(string) || type == typeof(Uri)) return SettingKind.String;
        if (type.IsEnum) return SettingKind.Enum;
        if (type == typeof(TimeSpan)) return SettingKind.TimeSpan;
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return SettingKind.Number;

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
                return SettingKind.Integer;
        }

        return IsStringList(type) ? SettingKind.StringList : SettingKind.ReadOnly;
    }

    private static bool IsStringList(Type type)
    {
        if (type == typeof(string[])) return true;
        if (!type.IsGenericType) return false;

        var definition = type.GetGenericTypeDefinition();
        return type.GetGenericArguments()[0] == typeof(string)
               && (definition == typeof(List<>)
                   || definition == typeof(IList<>)
                   || definition == typeof(IEnumerable<>)
                   || definition == typeof(ICollection<>)
                   || definition == typeof(IReadOnlyList<>)
                   || definition == typeof(IReadOnlyCollection<>));
    }
}
