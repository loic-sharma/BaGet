using System.ComponentModel.DataAnnotations;
using Xunit;

namespace BaGet.Core.Tests.Validation;

public class RequiredIfAttributeTests
{
    private class Model
    {
        public string? Mode { get; set; }

        public string? Value { get; set; }
    }

    private static ValidationResult? Validate(
        RequiredIfAttribute attribute,
        string? mode,
        string? value,
        string displayName = "Value")
    {
        var model = new Model { Mode = mode, Value = value };
        var context = new ValidationContext(model) { DisplayName = displayName };

        return attribute.GetValidationResult(value, context);
    }

    [Fact]
    public void RequiresTheValueWhenTheOtherPropertyMatches()
    {
        var result = Validate(new RequiredIfAttribute("Mode", "needed"), "needed", null);

        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void AcceptsAValueWhenTheOtherPropertyMatches()
    {
        var result = Validate(new RequiredIfAttribute("Mode", "needed"), "needed", "provided");

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IgnoresAMissingValueWhenTheOtherPropertyDoesNotMatch()
    {
        var result = Validate(new RequiredIfAttribute("Mode", "needed"), "other", null);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TreatsAWhitespaceOnlyStringAsMissing(string? value)
    {
        var result = Validate(new RequiredIfAttribute("Mode", "needed"), "needed", value);

        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void InvertedRequiresTheValueWhenTheOtherPropertyDiffers()
    {
        var attribute = new RequiredIfAttribute("Mode", "ignored") { IsInverted = true };

        var result = Validate(attribute, "something else", null);

        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void InvertedIgnoresAMissingValueWhenTheOtherPropertyMatches()
    {
        var attribute = new RequiredIfAttribute("Mode", "ignored") { IsInverted = true };

        var result = Validate(attribute, "ignored", null);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void MatchesANullOtherPropertyValue()
    {
        var result = Validate(new RequiredIfAttribute("Mode", null), null, null);

        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void ReportsAnUnknownOtherProperty()
    {
        var result = Validate(new RequiredIfAttribute("NoSuchProperty", "x"), "x", null);

        Assert.NotEqual(ValidationResult.Success, result);
        Assert.Contains("NoSuchProperty", result?.ErrorMessage);
    }

    [Fact]
    public void TheMessageNamesThePropertyAndTheCondition()
    {
        var result = Validate(new RequiredIfAttribute("Mode", "needed"), "needed", null);

        Assert.Contains("Value", result?.ErrorMessage);
        Assert.Contains("Mode", result?.ErrorMessage);
        Assert.Contains("needed", result?.ErrorMessage);
        Assert.Contains("of ", result?.ErrorMessage);
    }

    [Fact]
    public void TheInvertedMessageSaysOtherThan()
    {
        var attribute = new RequiredIfAttribute("Mode", "ignored") { IsInverted = true };

        var result = Validate(attribute, "something else", null);

        Assert.Contains("other than ", result?.ErrorMessage);
    }

    [Fact]
    public void TheMessageUsesTheOtherPropertyDisplayNameWhenSet()
    {
        var attribute = new RequiredIfAttribute("Mode", "needed")
        {
            OtherPropertyDisplayName = "Operating mode",
        };

        var result = Validate(attribute, "needed", null);

        Assert.Contains("Operating mode", result?.ErrorMessage);
    }

    [Fact]
    public void RequiresAValidationContext()
    {
        Assert.True(new RequiredIfAttribute("Mode", "needed").RequiresValidationContext);
    }
}