using Xunit;

namespace BaGet.Core.Tests.Formats;

/// <summary>A lookup accepts npm's legacy upper case names, a change keeps the strict rule; both stay safe path segments.</summary>
public class NpmNamesLookupTests
{
    [Theory]
    [InlineData("JSONStream")]
    [InlineData("@Scope/Name")]
    [InlineData("@scope%2FName")]
    public void ALookupAcceptsUpperCase(string name)
    {
        Assert.True(NpmNames.TryParseLookup(name, out var normalized, out _));
        Assert.Equal(System.Uri.UnescapeDataString(name), normalized);
        Assert.False(NpmNames.TryParse(name, out _, out _));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("_Hidden")]
    [InlineData(".Hidden")]
    [InlineData("-")]
    [InlineData("Node_Modules")]
    [InlineData("a b")]
    [InlineData("a*b")]
    [InlineData("@/x")]
    [InlineData("")]
    public void ALookupStillRefusesWhatIsNotASafeName(string name)
        => Assert.False(NpmNames.TryParseLookup(name, out _, out _));

    [Fact]
    public void ALowerCaseNameIsTheSameForBoth()
    {
        Assert.True(NpmNames.TryParse("left-pad", out var strict, out _));
        Assert.True(NpmNames.TryParseLookup("left-pad", out var lookup, out _));
        Assert.Equal(strict, lookup);
    }
}
