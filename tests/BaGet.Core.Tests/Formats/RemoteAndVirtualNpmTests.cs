using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Formats;

/// <summary>The pure parts of remote and virtual npm repositories: settings by format, the bearer credential, tarball URLs, the packument merge.</summary>
public class RemoteAndVirtualNpmTests
{
    private static JsonObject Version(string name, string version, string integrity = "sha512-x")
        => new()
        {
            ["name"] = name,
            ["version"] = version,
            ["dist"] = new JsonObject { ["tarball"] = $"http://h/{name}/-/{name}-{version}.tgz", ["integrity"] = integrity },
        };

    private static JsonObject Packument(string name, (string Version, string Time)[] versions, Dictionary<string, string> tags, string created = "2020-01-01T00:00:00.000Z", string modified = "2020-06-01T00:00:00.000Z", string? rev = null, string? description = null)
    {
        var versionNodes = new JsonObject();
        var time = new JsonObject { ["created"] = created, ["modified"] = modified };
        foreach (var v in versions)
        {
            versionNodes[v.Version] = Version(name, v.Version, "sha512-" + created);
            time[v.Version] = v.Time;
        }

        var tagNodes = new JsonObject();
        foreach (var tag in tags) tagNodes[tag.Key] = tag.Value;

        var document = new JsonObject { ["name"] = name, ["dist-tags"] = tagNodes, ["versions"] = versionNodes, ["time"] = time };
        if (rev != null) document["_rev"] = rev;
        if (description != null) document["description"] = description;

        return document;
    }

    // ---- settings ----

    [Fact]
    public void ANpmRepositoryAlwaysSpeaksTheNpmProtocolAndANuGetOneNeverDoes()
    {
        var posted = new RemoteRepositorySettings { Url = "https://registry.npmjs.org/", Protocol = RemoteProtocol.V3 };

        Assert.Equal(RemoteProtocol.Npm, posted.ForFormat(RepositoryFormat.Npm).Protocol);
        Assert.Equal(RemoteProtocol.V3, new RemoteRepositorySettings { Protocol = RemoteProtocol.Npm }.ForFormat(RepositoryFormat.NuGet).Protocol);
        Assert.Equal(RemoteProtocol.V2, new RemoteRepositorySettings { Protocol = RemoteProtocol.V2 }.ForFormat(RepositoryFormat.NuGet).Protocol);
        Assert.Equal(RemoteProtocol.V3, posted.Protocol);
    }

    [Fact]
    public void AnNpmUpstreamUrlMustBeAnAbsoluteUrlWithoutCredentialsOrQuery()
    {
        Assert.Empty(new RemoteRepositorySettings { Url = "https://registry.npmjs.org/", Protocol = RemoteProtocol.Npm }.Validate());
        Assert.Contains("query", string.Join(" ", new RemoteRepositorySettings { Url = "https://r.org/?a=b", Protocol = RemoteProtocol.Npm }.Validate()));
        Assert.Contains("credentials", string.Join(" ", new RemoteRepositorySettings { Url = "https://u:p@r.org/", Protocol = RemoteProtocol.Npm }.Validate()));
        Assert.NotEmpty(new RemoteRepositorySettings { Url = "ftp://r.org/", Protocol = RemoteProtocol.Npm }.Validate());
    }

    [Fact]
    public void TheNpmProtocolIsStoredByName()
    {
        var json = new RemoteSettingsCodec().Serialize(new RemoteRepositorySettings { Url = "https://r.org/", Protocol = RemoteProtocol.Npm });

        Assert.Contains("\"Npm\"", json);
        Assert.Equal(RemoteProtocol.Npm, new RemoteSettingsCodec().Deserialize(json)!.Protocol);
    }

    // ---- credentials ----

    private sealed class Capture : HttpMessageHandler
    {
        public string? Authorization;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task ATokenAloneIsABearerForNpmAndStaysOnTheUpstreamsOrigin()
    {
        var capture = new Capture();
        var handler = new HostScopedBasicAuthHandler(new Uri("https://registry.example.org/"), null, "tok", bearer: true) { InnerHandler = capture };
        using var client = new HttpClient(handler);

        using (await client.GetAsync("https://registry.example.org/left-pad", TestContext.Current.CancellationToken)) { }
        Assert.Equal("Bearer tok", capture.Authorization);

        using (await client.GetAsync("https://cdn.example.org/left-pad.tgz", TestContext.Current.CancellationToken)) { }
        Assert.Null(capture.Authorization);
    }

    // ---- tarball addresses ----

    [Theory]
    [InlineData("https://registry.npmjs.org/left-pad/-/left-pad-1.0.0.tgz", "https://registry.npmjs.org/left-pad/-/left-pad-1.0.0.tgz")]
    [InlineData("https://REGISTRY.npmjs.org/custom/path/x.tgz", "https://REGISTRY.npmjs.org/custom/path/x.tgz")]
    [InlineData("https://evil.example.org/left-pad-1.0.0.tgz", "https://registry.npmjs.org/left-pad/-/left-pad-1.0.0.tgz")]
    [InlineData("http://registry.npmjs.org/left-pad-1.0.0.tgz", "https://registry.npmjs.org/left-pad/-/left-pad-1.0.0.tgz")]
    [InlineData("https://registry.npmjs.org:8443/left-pad-1.0.0.tgz", "https://registry.npmjs.org/left-pad/-/left-pad-1.0.0.tgz")]
    [InlineData(null, "https://registry.npmjs.org/left-pad/-/left-pad-1.0.0.tgz")]
    [InlineData("not a url", "https://registry.npmjs.org/left-pad/-/left-pad-1.0.0.tgz")]
    public void ATarballIsFetchedFromTheUpstreamsOwnOriginOnly(string? announced, string expected)
    {
        var settings = new RemoteRepositorySettings { Url = "https://registry.npmjs.org", Protocol = RemoteProtocol.Npm };

        Assert.Equal(expected, NpmUpstreamClient.TarballUri(settings, "left-pad", "1.0.0", announced).OriginalString);
    }

    [Fact]
    public void AScopedTarballKeepsTheEncodedSlashOnTheConventionalPath()
    {
        var settings = new RemoteRepositorySettings { Url = "https://r.org/npm/", Protocol = RemoteProtocol.Npm };

        Assert.Equal("https://r.org/npm/@acme%2ftool/-/tool-2.0.0.tgz", NpmUpstreamClient.TarballUri(settings, "@acme/tool", "2.0.0", null).OriginalString);
    }

    [Fact]
    public void CacheKeysWriteTheScopeSlashAsAnEscapeSoAPurgeFindsThem()
    {
        Assert.Equal("@acme%2ftool", NpmUpstreamClient.KeyName("@acme/tool"));
        Assert.Equal("left-pad", NpmUpstreamClient.KeyName("left-pad"));
    }

    // ---- merge ----

    [Fact]
    public void TheMergeTakesEachVersionFromTheFirstMemberThatHasItAndKeepsThemInVersionOrder()
    {
        var first = Packument("pkg", new[] { ("1.0.0", "2020-01-02T00:00:00.000Z"), ("1.10.0", "2020-01-03T00:00:00.000Z") }, new() { ["latest"] = "1.10.0" }, created: "FIRST");
        var second = Packument("pkg", new[] { ("1.0.0", "2019-01-02T00:00:00.000Z"), ("1.2.0", "2019-02-02T00:00:00.000Z") }, new() { ["latest"] = "1.2.0" }, created: "SECOND");

        var merged = VirtualNpmRegistry.MergePackuments("pkg", new[] { first, second }, abbreviated: false);

        Assert.Equal(new[] { "1.0.0", "1.2.0", "1.10.0" }, ((JsonObject)merged["versions"]!).Select(v => v.Key));
        Assert.Equal("sha512-FIRST", (string?)merged["versions"]!["1.0.0"]!["dist"]!["integrity"]);
        Assert.Equal("sha512-SECOND", (string?)merged["versions"]!["1.2.0"]!["dist"]!["integrity"]);
        Assert.Equal("2020-01-02T00:00:00.000Z", (string?)merged["time"]!["1.0.0"]);
        Assert.Equal("2019-02-02T00:00:00.000Z", (string?)merged["time"]!["1.2.0"]);
    }

    [Fact]
    public void ABuildMetadataVariantOfAVersionAnEarlierMemberHasIsTheSameVersion()
    {
        var first = Packument("pkg", new[] { ("1.0.0", "t") }, new() { ["latest"] = "1.0.0" });
        var second = Packument("pkg", new[] { ("1.0.0+build.5", "t") }, new() { ["latest"] = "1.0.0+build.5" });

        var merged = VirtualNpmRegistry.MergePackuments("pkg", new[] { first, second }, abbreviated: false);

        Assert.Equal(new[] { "1.0.0" }, ((JsonObject)merged["versions"]!).Select(v => v.Key));
        Assert.Equal("1.0.0", (string?)merged["dist-tags"]!["latest"]);
    }

    [Fact]
    public void ATagTakesTheFirstMembersValueAndATagOnlyALaterMemberHasIsAdded()
    {
        var first = Packument("pkg", new[] { ("1.0.0", "t") }, new() { ["latest"] = "1.0.0" });
        var second = Packument("pkg", new[] { ("2.0.0", "t") }, new() { ["latest"] = "2.0.0", ["next"] = "2.0.0" });

        var merged = VirtualNpmRegistry.MergePackuments("pkg", new[] { first, second }, abbreviated: false);

        Assert.Equal("1.0.0", (string?)merged["dist-tags"]!["latest"]);
        Assert.Equal("2.0.0", (string?)merged["dist-tags"]!["next"]);
    }

    [Fact]
    public void ATagPointingAtAVersionTheMergeDoesNotHaveIsDroppedAndLatestFallsBackToTheHighest()
    {
        var broken = Packument("pkg", new[] { ("1.0.0", "t") }, new() { ["latest"] = "9.9.9", ["beta"] = "1.0.0" });

        var merged = VirtualNpmRegistry.MergePackuments("pkg", new[] { broken }, abbreviated: false);

        Assert.Equal("1.0.0", (string?)merged["dist-tags"]!["latest"]);
        Assert.Equal("1.0.0", (string?)merged["dist-tags"]!["beta"]);
    }

    [Fact]
    public void TheTimeKeepsTheEarliestCreatedAndTheLatestModifiedAndTheOtherFieldsComeFromTheFirstMember()
    {
        var first = Packument("pkg", new[] { ("1.0.0", "t") }, new() { ["latest"] = "1.0.0" }, created: "2020-03-01T00:00:00.000Z", modified: "2020-04-01T00:00:00.000Z", rev: "1-a", description: "first words");
        var second = Packument("pkg", new[] { ("2.0.0", "t") }, new() { ["latest"] = "2.0.0" }, created: "2019-03-01T00:00:00.000Z", modified: "2021-04-01T00:00:00.000Z", rev: "5-b", description: "second words");
        second["readme"] = "# only second has it";

        var merged = VirtualNpmRegistry.MergePackuments("pkg", new[] { first, second }, abbreviated: false);

        Assert.Equal("2019-03-01T00:00:00.000Z", (string?)merged["time"]!["created"]);
        Assert.Equal("2021-04-01T00:00:00.000Z", (string?)merged["time"]!["modified"]);
        Assert.Equal("first words", (string?)merged["description"]);
        Assert.Equal("# only second has it", (string?)merged["readme"]);
        Assert.StartsWith("2-", (string?)merged["_rev"]);
        Assert.Equal("pkg", (string?)merged["_id"]);
    }

    [Fact]
    public void TheAbbreviatedMergeHasTheInstallFieldsOnly()
    {
        var first = Packument("pkg", new[] { ("1.0.0", "t") }, new() { ["latest"] = "1.0.0" }, rev: "1-a");
        first["modified"] = "2020-06-01T00:00:00.000Z";

        var merged = VirtualNpmRegistry.MergePackuments("pkg", new[] { first }, abbreviated: true);

        Assert.Null(merged["time"]);
        Assert.Null(merged["_rev"]);
        Assert.Null(merged["_id"]);
        Assert.NotNull(merged["modified"]);
        Assert.NotNull(merged["versions"]);
        Assert.NotNull(merged["dist-tags"]);
    }

    [Fact]
    public void TheMergeDoesNotChangeTheDocumentsItReads()
    {
        var first = Packument("pkg", new[] { ("1.0.0", "t") }, new() { ["latest"] = "1.0.0" });
        var before = first.ToJsonString();

        var merged = VirtualNpmRegistry.MergePackuments("pkg", new[] { first }, abbreviated: false);
        merged["versions"]!["1.0.0"]!["dist"]!["tarball"] = "changed";

        Assert.Equal(before, first.ToJsonString());
    }
}
