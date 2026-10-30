using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace BaGet.Core.Tests.Settings;

public class SettingsFormTests
{
    private enum Color
    {
        Red,
        Green,
    }

    private static SettingState State(
        string key,
        SettingKind kind,
        string? value = null,
        bool sensitive = false,
        bool overridden = false,
        Type? type = null,
        string[]? enumValues = null)
        => new(
            new SettingDescriptor(key, key.Split(':')[0], "Test", kind, type ?? typeof(string), enumValues ?? Array.Empty<string>(), sensitive, false, string.Empty),
            sensitive ? null : value,
            !string.IsNullOrEmpty(value),
            overridden,
            null,
            false);

    private static IEnumerable<KeyValuePair<string, StringValues>> Form(params (string Key, string[] Values)[] fields)
        => fields.Select(f => new KeyValuePair<string, StringValues>(f.Key, new StringValues(f.Values)));

    private static (IReadOnlyDictionary<SettingDescriptor, object> Changes, IReadOnlyList<string> Errors) Parse(
        SettingState state,
        params (string Key, string[] Values)[] fields)
        => SettingsForm.Parse(Form(fields), new[] { state });

    [Fact]
    public void ABooleanCheckboxWinsOverTheHiddenFalse()
    {
        var (changes, errors) = Parse(State("A:Flag", SettingKind.Boolean, "False", type: typeof(bool)), ("v:A:Flag", new[] { "true", "false" }));

        Assert.Empty(errors);
        Assert.Equal(true, Assert.Single(changes).Value);
    }

    [Fact]
    public void AnUncheckedBooleanBecomesFalse()
    {
        var (changes, _) = Parse(State("A:Flag", SettingKind.Boolean, "True", type: typeof(bool)), ("v:A:Flag", new[] { "false" }));

        Assert.Equal(false, Assert.Single(changes).Value);
    }

    [Fact]
    public void AValueEqualToTheEffectiveOneIsNotAChange()
    {
        var (changes, errors) = Parse(State("A:Flag", SettingKind.Boolean, "True", type: typeof(bool)), ("v:A:Flag", new[] { "true", "false" }));

        Assert.Empty(errors);
        Assert.Empty(changes);
    }

    [Fact]
    public void AnIntegerParsesInvariantly()
    {
        var (changes, errors) = Parse(State("A:N", SettingKind.Integer, "1", type: typeof(int)), ("v:A:N", new[] { "1234" }));

        Assert.Empty(errors);
        Assert.Equal(1234L, Assert.Single(changes).Value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void AnInvalidIntegerIsReportedWithItsKey(string text)
    {
        var (changes, errors) = Parse(State("A:N", SettingKind.Integer, "1", type: typeof(int)), ("v:A:N", new[] { text }));

        Assert.Empty(changes);
        Assert.StartsWith("A:N: ", Assert.Single(errors));
    }

    [Fact]
    public void ABlankNumberIsLeftUnchanged()
    {
        var (changes, errors) = Parse(State("A:N", SettingKind.Integer, "1", type: typeof(int)), ("v:A:N", new[] { "  " }));

        Assert.Empty(errors);
        Assert.Empty(changes);
    }

    [Fact]
    public void ANumberUsesTheInvariantCulture()
    {
        var (changes, errors) = Parse(State("A:R", SettingKind.Number, "1", type: typeof(double)), ("v:A:R", new[] { "2.5" }));

        Assert.Empty(errors);
        Assert.Equal(2.5d, Assert.Single(changes).Value);
    }

    [Fact]
    public void ATimeSpanIsParsed()
    {
        var (changes, errors) = Parse(State("A:T", SettingKind.TimeSpan, "00:10:00", type: typeof(TimeSpan)), ("v:A:T", new[] { "00:20:00" }));

        Assert.Empty(errors);
        Assert.Equal(TimeSpan.FromMinutes(20), Assert.Single(changes).Value);
    }

    [Fact]
    public void AnInvalidTimeSpanIsReported()
    {
        var (_, errors) = Parse(State("A:T", SettingKind.TimeSpan, "00:10:00", type: typeof(TimeSpan)), ("v:A:T", new[] { "soon" }));

        Assert.StartsWith("A:T: ", Assert.Single(errors));
    }

    [Fact]
    public void AnEnumMatchesIgnoringCaseAndKeepsTheCanonicalName()
    {
        var state = State("A:C", SettingKind.Enum, "Red", type: typeof(Color), enumValues: new[] { "Red", "Green" });

        var (changes, errors) = Parse(state, ("v:A:C", new[] { "green" }));

        Assert.Empty(errors);
        Assert.Equal("Green", Assert.Single(changes).Value);
    }

    [Fact]
    public void AnEnumOutsideTheAllowedNamesIsReported()
    {
        var state = State("A:C", SettingKind.Enum, "Red", type: typeof(Color), enumValues: new[] { "Red", "Green" });

        var (changes, errors) = Parse(state, ("v:A:C", new[] { "Blue" }));

        Assert.Empty(changes);
        Assert.Contains("Red, Green", Assert.Single(errors));
    }

    [Fact]
    public void AStringListIsOneEntryPerLineWithBlanksDropped()
    {
        var (changes, errors) = Parse(State("A:L", SettingKind.StringList, "x"), ("v:A:L", new[] { "a\r\n\r\n b \nc\n" }));

        Assert.Empty(errors);
        Assert.Equal(new[] { "a", "b", "c" }, (string[])Assert.Single(changes).Value);
    }

    [Fact]
    public void AStringListEqualToTheEffectiveOneIsNotAChange()
    {
        var (changes, _) = Parse(State("A:L", SettingKind.StringList, "a\nb"), ("v:A:L", new[] { "a\r\nb" }));

        Assert.Empty(changes);
    }

    [Fact]
    public void AStringCanBeEmptied()
    {
        var (changes, _) = Parse(State("A:S", SettingKind.String, "old"), ("v:A:S", new[] { "" }));

        Assert.Equal(string.Empty, Assert.Single(changes).Value);
    }

    [Fact]
    public void ASensitiveBlankMeansUnchanged()
    {
        var (changes, errors) = Parse(State("A:Secret", SettingKind.String, "set", sensitive: true), ("v:A:Secret", new[] { "" }));

        Assert.Empty(errors);
        Assert.Empty(changes);
    }

    [Fact]
    public void ASensitiveValueIsTaken()
    {
        var (changes, _) = Parse(State("A:Secret", SettingKind.String, "set", sensitive: true), ("v:A:Secret", new[] { "new" }));

        Assert.Equal("new", Assert.Single(changes).Value);
    }

    [Fact]
    public void ClearEmptiesASensitiveValue()
    {
        var (changes, _) = Parse(
            State("A:Secret", SettingKind.String, "set", sensitive: true),
            ("v:A:Secret", new[] { "ignored" }),
            ("clear:A:Secret", new[] { "on" }));

        Assert.Equal(string.Empty, Assert.Single(changes).Value);
    }

    [Fact]
    public void ClearOnAnUnsetSensitiveValueIsNotAChange()
    {
        var (changes, _) = Parse(State("A:Secret", SettingKind.String, null, sensitive: true), ("clear:A:Secret", new[] { "on" }));

        Assert.Empty(changes);
    }

    [Fact]
    public void ResetRemovesAnOverrideAndBeatsTheValue()
    {
        var (changes, _) = Parse(
            State("A:S", SettingKind.String, "saved", overridden: true),
            ("v:A:S", new[] { "other" }),
            ("reset:A:S", new[] { "on" }));

        var change = Assert.Single(changes);
        Assert.Null(change.Value);
    }

    [Fact]
    public void ResetOfASettingThatIsNotOverriddenIsIgnored()
    {
        var (changes, _) = Parse(State("A:S", SettingKind.String, "default"), ("reset:A:S", new[] { "on" }));

        Assert.Empty(changes);
    }

    [Fact]
    public void ReadOnlyAndAbsentFieldsAreIgnored()
    {
        var states = new[]
        {
            State("A:Map", SettingKind.ReadOnly, "x"),
            State("A:Other", SettingKind.String, "y"),
        };

        var (changes, errors) = SettingsForm.Parse(Form(("v:A:Map", new[] { "hacked" })), states);

        Assert.Empty(errors);
        Assert.Empty(changes);
    }

    [Fact]
    public void OneBadFieldDoesNotHideTheOtherChanges()
    {
        var states = new[]
        {
            State("A:N", SettingKind.Integer, "1", type: typeof(int)),
            State("A:S", SettingKind.String, "old"),
        };

        var (changes, errors) = SettingsForm.Parse(Form(("v:A:N", new[] { "x" }), ("v:A:S", new[] { "new" })), states);

        Assert.Single(errors);
        Assert.Equal("new", Assert.Single(changes).Value);
    }
}
