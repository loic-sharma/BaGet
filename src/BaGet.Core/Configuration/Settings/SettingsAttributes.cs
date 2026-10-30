using System;
using System.Reflection;

namespace BaGet.Core;

/// <summary>
/// Marks a setting whose value is a credential: the Settings page shows it masked, never
/// echoes it back, and the service never logs it.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveSettingAttribute : Attribute
{
}

/// <summary>
/// Marks a setting, or every setting of an options type, that is read once at startup
/// (at registration, in the pipeline setup, or through the singleton
/// <c>IOptions&lt;T&gt;</c>), so a change takes effect after a restart. A setting without it
/// is read through <c>IOptionsSnapshot&lt;T&gt;</c> and applies to the next request.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class RestartRequiredAttribute : Attribute
{
}

/// <summary>
/// Keeps a public settable property out of the Settings page: it is internal state that
/// the application sets itself, not something an operator configures.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HiddenSettingAttribute : Attribute
{
}

/// <summary>
/// Restricts a string setting to a fixed set of names. The Settings page shows it as a
/// drop-down (matching the current value case-insensitively), the form refuses any other
/// value, and startup validation reads the same <see cref="Values"/>, so the list lives in
/// one place. Comparison is case-insensitive.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AllowedSettingValuesAttribute : Attribute
{
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="values">The allowed names, in display order.</param>
    public AllowedSettingValuesAttribute(params string[] values)
    {
        Values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>The allowed names, in display order.</summary>
    public string[] Values { get; }

    /// <summary>
    /// The allowed names declared on a property.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="property">The property name, such as <c>nameof(DatabaseOptions.Type)</c>.</param>
    public static string[] ValuesOf<TOptions>(string property)
        => typeof(TOptions).GetProperty(property)?.GetCustomAttribute<AllowedSettingValuesAttribute>()?.Values
           ?? Array.Empty<string>();
}

/// <summary>
/// Gives a setting an example value that depends on another setting of the same options
/// type, such as a connection string per database type. The Settings page shows it as a
/// placeholder and a help line, and updates both when the other setting's drop-down changes.
/// The example is never written into the field's value. Repeat the attribute once per value; every one on a property must depend on the same property and name each value once, or the catalog throws <see cref="InvalidOperationException"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class SettingExampleAttribute : Attribute
{
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="dependsOn">The name of the property of the same type that selects the example.</param>
    /// <param name="whenValue">The value of that property (case-insensitive) this example is for.</param>
    /// <param name="example">The example value.</param>
    public SettingExampleAttribute(string dependsOn, string whenValue, string example)
    {
        DependsOn = dependsOn ?? throw new ArgumentNullException(nameof(dependsOn));
        WhenValue = whenValue ?? throw new ArgumentNullException(nameof(whenValue));
        Example = example ?? throw new ArgumentNullException(nameof(example));
    }

    /// <summary>The property that selects the example.</summary>
    public string DependsOn { get; }

    /// <summary>The value of <see cref="DependsOn"/> this example is for.</summary>
    public string WhenValue { get; }

    /// <summary>The example value.</summary>
    public string Example { get; }
}

/// <summary>
/// An options type bound to a configuration section. Recorded by
/// <see cref="DependencyInjectionExtensions.AddBaGetOptions{TOptions}"/> so that the
/// <see cref="SettingsCatalog"/> lists every options type, provider-specific ones included.
/// </summary>
/// <param name="OptionsType">The options type.</param>
/// <param name="Section">The configuration section it is bound to, or null for the root.</param>
public sealed record SettingsSchemaRegistration(Type OptionsType, string? Section);
