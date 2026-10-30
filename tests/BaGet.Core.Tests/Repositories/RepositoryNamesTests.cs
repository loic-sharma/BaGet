using Xunit;

namespace BaGet.Core.Tests.Repositories;

public class RepositoryNamesTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("team-a")]
    [InlineData("a")]
    [InlineData("my.repo_1")]
    [InlineData("0123456789")]
    public void AcceptsLowercaseLettersDigitsAndSeparators(string name)
    {
        Assert.True(RepositoryNames.IsValid(name, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void AcceptsTheMaximumLength()
        => Assert.True(RepositoryNames.IsValid(new string('a', RepositoryNames.MaxLength)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("slash/name")]
    [InlineData("café")]
    [InlineData("a:b")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData("a..b")]
    public void RejectsOtherCharactersAndEmptyNames(string? name)
    {
        Assert.False(RepositoryNames.IsValid(name, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void RejectsANameOverTheMaximumLength()
        => Assert.False(RepositoryNames.IsValid(new string('a', RepositoryNames.MaxLength + 1)));

    [Theory]
    [InlineData("r")]
    [InlineData("api")]
    [InlineData("v2")]
    [InlineData("v3")]
    [InlineData("admin")]
    [InlineData("account")]
    [InlineData("packages")]
    [InlineData("css")]
    [InlineData("favicon.ico")]
    public void RejectsNamesThatShadowARoute(string name)
    {
        Assert.False(RepositoryNames.IsValid(name, out var error));
        Assert.Contains("reserved", error);
    }
}
