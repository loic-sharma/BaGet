using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BaGet.Core.Tests.Formats;

/// <summary>The pure rules of a remote Docker repository: its settings, the challenge of a registry, the names it asks for.</summary>
public class OciUpstreamTests
{
    private static RemoteRepositorySettings Hub(string url = "https://registry-1.docker.io") => new() { Url = url, Protocol = RemoteProtocol.Docker };

    [Fact]
    public void ADockerRepositoryAlwaysSpeaksTheDockerProtocol()
    {
        var copy = new RemoteRepositorySettings { Url = "https://r.test", Protocol = RemoteProtocol.V2 }.ForFormat(RepositoryFormat.Docker);

        Assert.Equal(RemoteProtocol.Docker, copy.Protocol);
    }

    [Theory]
    [InlineData(RemoteProtocol.Docker)]
    [InlineData(RemoteProtocol.Maven)]
    [InlineData(RemoteProtocol.Npm)]
    public void ANuGetRepositoryNeverKeepsAnotherFormatsProtocol(RemoteProtocol protocol)
    {
        var copy = new RemoteRepositorySettings { Url = "https://r.test", Protocol = protocol }.ForFormat(RepositoryFormat.NuGet);

        Assert.Equal(RemoteProtocol.V3, copy.Protocol);
    }

    [Fact]
    public void ARegistryUrlMustNotCarryAQueryOrCredentials()
    {
        Assert.Empty(Hub().Validate());
        Assert.Contains(Hub("https://registry-1.docker.io/?x=1").Validate(), p => p.Contains("must not have a query string"));
        Assert.Contains(Hub("https://user:pw@registry-1.docker.io").Validate(), p => p.Contains("credentials"));
    }

    [Fact]
    public void ABlockedPrivateAddressIsRefusedForARegistryToo()
    {
        Assert.Contains(Hub("http://localhost:5000").Validate(blockPrivateAddresses: true), p => p.Contains("Remote:BlockPrivateAddresses"));
        Assert.Contains(Hub("http://10.0.0.5:5000").Validate(blockPrivateAddresses: true), p => p.Contains("Remote:BlockPrivateAddresses"));
        Assert.Empty(Hub("http://localhost:5000").Validate(blockPrivateAddresses: false));
    }

    [Theory]
    [InlineData("https://registry-1.docker.io", "nginx", "library/nginx")]
    [InlineData("https://index.docker.io/", "redis", "library/redis")]
    [InlineData("https://docker.io", "alpine", "library/alpine")]
    [InlineData("https://registry-1.docker.io", "grafana/grafana", "grafana/grafana")]
    [InlineData("https://registry-1.docker.io", "library/nginx", "library/nginx")]
    [InlineData("https://ghcr.io", "tool", "tool")]
    [InlineData("https://registry.test:5000", "tool", "tool")]
    public void OnlyDockerHubMovesASingleNameUnderLibrary(string url, string image, string expected)
        => Assert.Equal(expected, OciUpstreamClient.UpstreamName(Hub(url), image));

    [Theory]
    [InlineData("https://registry-1.docker.io", "https://registry-1.docker.io/")]
    [InlineData("https://registry-1.docker.io/", "https://registry-1.docker.io/")]
    [InlineData("https://registry-1.docker.io/v2", "https://registry-1.docker.io/")]
    [InlineData("https://registry-1.docker.io/v2/", "https://registry-1.docker.io/")]
    [InlineData("https://host.test/registry/v2/", "https://host.test/registry/")]
    public void TheRegistryAddressWithOrWithoutV2IsTheSame(string url, string expected)
        => Assert.Equal(expected, OciUpstreamClient.RegistryBase(Hub(url)));

    [Fact]
    public void ADockerHubChallengeIsReadIntoItsParts()
    {
        var parts = OciUpstreamClient.ParseChallenge("realm=\"https://auth.docker.io/token\",service=\"registry.docker.io\",scope=\"repository:library/nginx:pull\"");

        Assert.Equal("https://auth.docker.io/token", parts["realm"]);
        Assert.Equal("registry.docker.io", parts["service"]);
        Assert.Equal("repository:library/nginx:pull", parts["scope"]);
    }

    [Fact]
    public void AChallengeKeepsCommasAndEscapesInsideQuotesAndTakesUnquotedValues()
    {
        var parts = OciUpstreamClient.ParseChallenge("realm=\"https://a.test/t\",scope=\"repository:x/y:pull,push\",error=invalid_token,service=\"a \\\"b\\\"\"");

        Assert.Equal("repository:x/y:pull,push", parts["scope"]);
        Assert.Equal("invalid_token", parts["error"]);
        Assert.Equal("a \"b\"", parts["service"]);
    }

    [Fact]
    public void AMalformedChallengeGivesWhatItCanAndNeverThrows()
    {
        Assert.Empty(OciUpstreamClient.ParseChallenge(string.Empty));
        Assert.Empty(OciUpstreamClient.ParseChallenge("garbage"));
        Assert.Equal("x", OciUpstreamClient.ParseChallenge("realm=\"x\",broken")["realm"]);
    }

    [Theory]
    [InlineData("om:team/app|1.0", "team/app", true)]
    [InlineData("ob:team/app|sha256:abc", "team/app", true)]
    [InlineData("ol:team/app|all", "team/app", true)]
    [InlineData("om:team/application|1.0", "team/app", false)]
    [InlineData("om:team/other|1.0", "team/app", false)]
    public void APurgeFindsTheAnswersOfAnImageByItsName(string key, string image, bool matches)
        => Assert.Equal(matches, OciUpstreamClient.MatchesImage(key, image, reference: null));

    [Fact]
    public void APurgeOfOneReferenceKeepsTheOtherManifestsAndDropsTheTagLists()
    {
        Assert.True(OciUpstreamClient.MatchesImage("om:team/app|1.0", "team/app", "1.0"));
        Assert.False(OciUpstreamClient.MatchesImage("om:team/app|2.0", "team/app", "1.0"));
        Assert.True(OciUpstreamClient.MatchesImage("ol:team/app|all", "team/app", "1.0"));
    }

    [Fact]
    public void ATagListIsPagedByCodePointAfterLastAndSaysWhetherMoreFollow()
    {
        var names = new List<string> { "b", "a", "c", "10", "9" };

        var all = RemoteOciRegistry.Page("x", names, count: null, last: null);
        Assert.Equal(new[] { "10", "9", "a", "b", "c" }, all.Tags);
        Assert.False(all.HasMore);

        var page = RemoteOciRegistry.Page("x", names, count: 2, last: "9");
        Assert.Equal(new[] { "a", "b" }, page.Tags);
        Assert.True(page.HasMore);

        Assert.Empty(RemoteOciRegistry.Page("x", names, count: 0, last: null).Tags.ToList());
    }
}
