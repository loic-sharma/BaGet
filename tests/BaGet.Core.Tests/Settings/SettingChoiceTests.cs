using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace BaGet.Core.Tests.Settings;

/// <summary>
/// <see cref="AllowedSettingValuesAttribute"/> and <see cref="SettingExampleAttribute"/>:
/// the catalog, the form and the page helpers.
/// </summary>
public class SettingChoiceTests
{
    public class OtherStorageOptions
    {
        public string? Type { get; set; }
    }

    public class ExampleOptions
    {
        [AllowedSettingValues("Red", "Green")]
        public string? Color { get; set; }

        [SettingExample(nameof(Color), "Red", "r")]
        [SettingExample(nameof(Color), "Green", "g")]
        public string? Value { get; set; }
    }

    private static SettingsCatalog Catalog(params SettingsSchemaRegistration[] registrations) => new(registrations);

    private static SettingsCatalog Real() => Catalog(
        new SettingsSchemaRegistration(typeof(DatabaseOptions), "Database"),
        new SettingsSchemaRegistration(typeof(StorageOptions), "Storage"),
        new SettingsSchemaRegistration(typeof(SearchOptions), "Search"));

    [Theory]
    [InlineData("Database:Type", new[] { "AzureTable", "MySql", "PostgreSql", "Sqlite", "SqlServer" })]
    [InlineData("Storage:Type", new[] { "AliyunOss", "AwsS3", "AzureBlobStorage", "Filesystem", "GoogleCloud", "Null" })]
    [InlineData("Search:Type", new[] { "AzureSearch", "Database", "Null" })]
    public void TheTypeKeysAreChoicesFromTheAttribute(string key, string[] expected)
    {
        var descriptor = Real().Find(key)!;

        Assert.Equal(SettingKind.Enum, descriptor.Kind);
        Assert.Equal(expected, descriptor.EnumValues);
    }

    [Fact]
    public void TheCatalogAndTheStartupValidationShareOneList()
    {
        Assert.Equal(AllowedSettingValuesAttribute.ValuesOf<DatabaseOptions>(nameof(DatabaseOptions.Type)), Real().Find("Database:Type")!.EnumValues);
        Assert.Equal(AllowedSettingValuesAttribute.ValuesOf<StorageOptions>(nameof(StorageOptions.Type)), Real().Find("Storage:Type")!.EnumValues);
        Assert.Equal(AllowedSettingValuesAttribute.ValuesOf<SearchOptions>(nameof(SearchOptions.Type)), Real().Find("Search:Type")!.EnumValues);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharingAKeyWithATypeWithoutTheAttributeKeepsTheChoices(bool plainFirst)
    {
        var plain = new SettingsSchemaRegistration(typeof(OtherStorageOptions), "Storage");
        var choice = new SettingsSchemaRegistration(typeof(StorageOptions), "Storage");

        var descriptor = (plainFirst ? Catalog(plain, choice) : Catalog(choice, plain)).Find("Storage:Type")!;

        Assert.Equal(SettingKind.Enum, descriptor.Kind);
        Assert.Contains("Filesystem", descriptor.EnumValues);
    }

    [Fact]
    public void TheConnectionStringCarriesAnExamplePerType()
    {
        var descriptor = Real().Find("Database:ConnectionString")!;

        Assert.Equal("Database:Type", descriptor.ExampleDependsOnKey);
        Assert.Equal(
            new[] { "AzureTable", "MySql", "PostgreSql", "Sqlite", "SqlServer" },
            descriptor.Examples!.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal("Data Source=baget.db", descriptor.Examples["sqlite"]);
        Assert.True(descriptor.IsSensitive);
    }

    [Fact]
    public void AnExampleDependsOnASiblingProperty()
    {
        var descriptor = Catalog(new SettingsSchemaRegistration(typeof(ExampleOptions), "X")).Find("X:Value")!;

        Assert.Equal("X:Color", descriptor.ExampleDependsOnKey);
        Assert.Equal("g", descriptor.Examples!["GREEN"]);
    }

    // --- form ---

    private static SettingState State(string? value, string[]? allowed = null)
        => new(
            new SettingDescriptor("Database:Type", "Database", "Database", SettingKind.Enum, typeof(string), allowed ?? new[] { "Sqlite", "MySql" }, false, true, string.Empty),
            value, !string.IsNullOrEmpty(value), false, null, false);

    private static (IReadOnlyDictionary<SettingDescriptor, object> Changes, IReadOnlyList<string> Errors) Post(SettingState state, string posted)
        => SettingsForm.Parse(new[] { new KeyValuePair<string, StringValues>("v:Database:Type", posted) }, new[] { state });

    [Fact]
    public void AValueOutsideTheListIsRejected()
    {
        var (changes, errors) = Post(State("Sqlite"), "Oracle");

        Assert.Empty(changes);
        Assert.Contains("Oracle", Assert.Single(errors));
        Assert.Contains("Sqlite, MySql", errors[0]);
    }

    [Fact]
    public void ACaseVariantIsAcceptedAndSavedWithTheListedSpelling()
    {
        var (changes, errors) = Post(State("Sqlite"), "mysql");

        Assert.Empty(errors);
        Assert.Equal("MySql", Assert.Single(changes).Value);
    }

    [Fact]
    public void ACaseVariantOfTheCurrentValueIsNotAChange()
    {
        var (changes, errors) = Post(State("Sqlite"), "SQLITE");

        Assert.Empty(errors);
        Assert.Empty(changes);
    }

    [Fact]
    public void AnUnchangedCurrentValueOutsideTheListIsAccepted()
    {
        var (changes, errors) = Post(State("Oracle"), "Oracle");

        Assert.Empty(errors);
        Assert.Empty(changes);
    }

    // --- page helpers ---

    [Fact]
    public void TheCurrentValueIsSelectedIgnoringCase()
    {
        var choices = SettingPresentation.Choices(State("x", new[] { "Filesystem", "Null" }).Descriptor, "FileSystem");

        Assert.Equal(new[] { "Filesystem", "Null" }, choices.Select(c => c.Value));
        Assert.Equal("Filesystem", Assert.Single(choices, c => c.Selected).Value);
    }

    [Theory]
    [InlineData("Oracle", "Oracle")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void AnUnknownOrEmptyCurrentValueIsKeptAsTheSelectedEntry(string? current, string postedValue)
    {
        var choices = SettingPresentation.Choices(State("x").Descriptor, current);

        var selected = Assert.Single(choices, c => c.Selected);
        Assert.Equal(postedValue, selected.Value);
        Assert.Equal(3, choices.Count);
        if (current == "Oracle") Assert.Contains("Oracle", selected.Label);
        else Assert.Contains("not set", selected.Label);
    }

    private static SettingState ConnectionString(bool hasValue)
        => new(
            new SettingDescriptor(
                "Database:ConnectionString", "Database", "Database", SettingKind.String, typeof(string), Array.Empty<string>(), true, true, string.Empty,
                "Database:Type", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Sqlite"] = "Data Source=baget.db" }),
            null, hasValue, false, null, false);

    [Fact]
    public void AnUnsetSecretShowsTheExampleAsPlaceholder()
        => Assert.Equal("e.g. Data Source=baget.db", SettingPresentation.Placeholder(ConnectionString(false), "sqlite"));

    [Fact]
    public void AStoredSecretKeepsTheUnchangedPlaceholder()
        => Assert.Equal("•••• unchanged", SettingPresentation.Placeholder(ConnectionString(true), "Sqlite"));

    [Fact]
    public void WithoutAnExampleThePlaceholderSaysNotSet()
    {
        Assert.Equal("not set", SettingPresentation.Placeholder(ConnectionString(false), "Oracle"));
        Assert.Equal("not set", SettingPresentation.Placeholder(ConnectionString(false), null));
    }

    [Fact]
    public void TheExampleIsLookedUpIgnoringCaseAndTheSelectorIsFoundAmongTheSiblings()
    {
        var connection = ConnectionString(false);
        var type = State("sqlite");

        Assert.Equal("sqlite", SettingPresentation.DependsValue(connection.Descriptor, new[] { connection, type }));
        Assert.Equal("Data Source=baget.db", SettingPresentation.ExampleFor(connection.Descriptor, "SQLITE"));
        Assert.Null(SettingPresentation.ExampleFor(connection.Descriptor, "Oracle"));
        Assert.Null(SettingPresentation.ExampleFor(State("x").Descriptor, "Sqlite"));
    }

    [Fact]
    public void TheExamplesJsonKeysAreLowerCasedValuesOfAnyCharacters()
    {
        var json = SettingPresentation.ExamplesJson(new Dictionary<string, string>
        {
            ["Source"] = "a",
            ["Target"] = "b",
            ["a b\"<"] = "<c>",
        });

        var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("a", parsed["source"]);
        Assert.Equal("b", parsed["target"]);
        Assert.Equal("<c>", parsed["a b\"<"]);
        Assert.DoesNotContain("<", json);
    }

    public class DuplicateExampleOptions
    {
        public string? Color { get; set; }

        [SettingExample(nameof(Color), "Red", "r")]
        [SettingExample(nameof(Color), "RED", "R")]
        public string? Value { get; set; }
    }

    public class MixedDependsOnOptions
    {
        public string? Color { get; set; }

        public string? Size { get; set; }

        [SettingExample(nameof(Color), "Red", "r")]
        [SettingExample(nameof(Size), "Big", "b")]
        public string? Value { get; set; }
    }

    [Theory]
    [InlineData(typeof(DuplicateExampleOptions), "Value", "RED")]
    [InlineData(typeof(MixedDependsOnOptions), "Value", "Size")]
    public void InvalidExamplesFailTheCatalogNamingTheProperty(Type type, string property, string mention)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Catalog(new SettingsSchemaRegistration(type, "Test")));

        Assert.Contains("Test:" + property, ex.Message);
        Assert.Contains(mention, ex.Message);
    }
}
