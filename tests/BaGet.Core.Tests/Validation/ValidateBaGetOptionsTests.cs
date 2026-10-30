using System.ComponentModel.DataAnnotations;
using System.Linq;
using Xunit;

namespace BaGet.Core.Tests.Validation;

public class ValidateBaGetOptionsTests
{
    private class Options
    {
        [Required]
        public string? Name { get; set; }

        [Range(1, 10)]
        public int Count { get; set; } = 1;
    }

    [Fact]
    public void AcceptsValidOptions()
    {
        var target = new ValidateBaGetOptions<Options>("Section");

        var result = target.Validate(null, new Options { Name = "ok", Count = 5 });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void RejectsAMissingRequiredValue()
    {
        var target = new ValidateBaGetOptions<Options>("Section");

        var result = target.Validate(null, new Options { Name = null });

        Assert.True(result.Failed);
    }

    [Fact]
    public void RejectsAnOutOfRangeValue()
    {
        var target = new ValidateBaGetOptions<Options>("Section");

        var result = target.Validate(null, new Options { Name = "ok", Count = 99 });

        Assert.True(result.Failed);
    }

    [Fact]
    public void ValidatesEveryPropertyNotJustTheFirst()
    {
        var target = new ValidateBaGetOptions<Options>("Section");

        var result = target.Validate(null, new Options { Name = null, Count = 99 });

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures.Count());
    }

    [Fact]
    public void TheMessageNamesTheConfigurationSection()
    {
        var target = new ValidateBaGetOptions<Options>("Storage");

        var result = target.Validate(null, new Options { Name = null });

        Assert.Contains("Invalid 'Storage' configs", result.FailureMessage);
    }

    [Fact]
    public void TheMessageOmitsTheSectionForRootOptions()
    {
        var target = new ValidateBaGetOptions<Options>(null);

        var result = target.Validate(null, new Options { Name = null });

        Assert.Contains("Invalid configs", result.FailureMessage);
        Assert.DoesNotContain("''", result.FailureMessage);
    }

    [Fact]
    public void TheMessageIncludesTheValidationError()
    {
        var target = new ValidateBaGetOptions<Options>("Section");

        var result = target.Validate(null, new Options { Name = null });

        Assert.Contains("Name", result.FailureMessage);
    }
}