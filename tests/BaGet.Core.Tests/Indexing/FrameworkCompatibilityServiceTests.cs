using Xunit;

namespace BaGet.Core.Tests.Indexing;

public class FrameworkCompatibilityServiceTests
{
    private readonly FrameworkCompatibilityService _target = new();

    [Fact]
    public void AlwaysIncludesTheAnyFramework()
    {
        // A package whose assets are under "any" applies to every target framework.
        Assert.Contains("any", _target.FindAllCompatibleFrameworks("net6.0"));
    }

    [Fact]
    public void IncludesTheTargetFrameworkItself()
    {
        Assert.Contains("net6.0", _target.FindAllCompatibleFrameworks("net6.0"));
    }

    [Theory]
    [InlineData("net6.0", "net5.0")]
    [InlineData("net5.0", "netcoreapp3.1")]
    [InlineData("netstandard2.1", "netstandard2.0")]
    [InlineData("netstandard2.0", "netstandard1.0")]
    public void IncludesAnEarlierVersionOfTheSameFramework(string target, string earlier)
    {
        Assert.Contains(earlier, _target.FindAllCompatibleFrameworks(target));
    }

    [Theory]
    [InlineData("net5.0", "net6.0")]
    [InlineData("netstandard2.0", "netstandard2.1")]
    public void ExcludesALaterVersionOfTheSameFramework(string target, string later)
    {
        Assert.DoesNotContain(later, _target.FindAllCompatibleFrameworks(target));
    }

    [Fact]
    public void ANetCoreAppTargetIsCompatibleWithNetStandard()
    {
        var result = _target.FindAllCompatibleFrameworks("netcoreapp3.1");

        Assert.Contains("netstandard2.0", result);
    }

    [Fact]
    public void ANetFrameworkTargetIsCompatibleWithNetStandard()
    {
        var result = _target.FindAllCompatibleFrameworks("net472");

        Assert.Contains("netstandard2.0", result);
    }

    [Fact]
    public void ANetStandardTargetIsNotCompatibleWithNetCoreApp()
    {
        // A netstandard library cannot consume a netcoreapp asset.
        var result = _target.FindAllCompatibleFrameworks("netstandard2.0");

        Assert.DoesNotContain("netcoreapp3.1", result);
    }

    [Theory]
    [InlineData("net471")]
    [InlineData("net472")]
    public void KnowsTheFrameworksAddedOnTopOfCommonFrameworks(string target)
    {
        // These two are absent from NuGet's CommonFrameworks and are registered by hand,
        // so an unknown-framework result would mean the registration was lost.
        var result = _target.FindAllCompatibleFrameworks(target);

        Assert.Contains("netstandard2.0", result);
        Assert.Contains(target, result);
    }

    [Fact]
    public void AnUnknownFrameworkFallsBackToItselfAndAny()
    {
        var result = _target.FindAllCompatibleFrameworks("not-a-framework");

        Assert.Equal(new[] { "not-a-framework", "any" }, result);
    }

    [Fact]
    public void RepeatedCallsReturnTheSameResult()
    {
        // The results are memoised in a static cache shared by every instance.
        var first = _target.FindAllCompatibleFrameworks("net6.0");
        var second = new FrameworkCompatibilityService().FindAllCompatibleFrameworks("net6.0");

        Assert.Equal(first, second);
    }

    [Fact]
    public void NeverReturnsDuplicates()
    {
        var result = _target.FindAllCompatibleFrameworks("net6.0");

        Assert.Equal(result.Count, new System.Collections.Generic.HashSet<string>(result).Count);
    }
}