using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Virtual npm repositories (P3): <c>loc</c> (local), <c>up</c> (a remote repository over a <see cref="FakeNpmUpstream"/>) and
/// <c>v</c> = [loc, up] deploying to <c>loc</c>. Reads merge the members in order, writes go to the deploy repository, a member the
/// caller cannot read is invisible, a failing member is skipped.
/// </summary>
public class VirtualNpmRepositoryTests : IDisposable
{
    private const string Key = "secret-key";
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeNpmUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public VirtualNpmRepositoryTests(ITestOutputHelper output)
    {
        _wire = new ForwardingRemoteHttpClientFactory(new HttpClient(_upstream));
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", Key } },
            configureServices: services => services.AddSingleton<IRemoteHttpClientFactory>(_wire));
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        _app.CreateNpmRepositoryAsync("loc").GetAwaiter().GetResult();
        CreateRemoteAsync("up").GetAwaiter().GetResult();
        CreateVirtualAsync("v", new[] { "loc", "up" }, "loc").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private async Task CreateRemoteAsync(string name, bool anonymousRead = true)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync(
            name,
            RepositoryFormat.Npm,
            RepositoryKind.Remote,
            new RemoteRepositorySettings { Url = "http://registry.test/", MetadataTtlSeconds = 600 },
            anonymousRead,
            Cancel);
    }

    private async Task CreateVirtualAsync(string name, string[] members, string? deploy, bool anonymousRead = true)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
            name,
            new VirtualRepositorySettings { Members = members.ToList(), DeployRepository = deploy },
            Cancel,
            actor: null,
            anonymousRead,
            RepositoryFormat.Npm);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null, string? token = null, string? accept = null)
    {
        using var request = NpmTestPackages.Json(method, path, body, token);
        if (accept != null) request.Headers.Accept.ParseAdd(accept);

        return await _client.SendAsync(request, Cancel);
    }

    private async Task<NpmTarballFile> PublishAsync(string repository, string name, string version, string tag = "latest")
    {
        var tarball = NpmTestPackages.Tarball(name, version);
        using var response = await SendAsync(HttpMethod.Put, NpmTestPackages.Path($"/r/{repository}/npm/", name), NpmTestPackages.PublishBody(tarball, tag), Key);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");

        return tarball;
    }

    private async Task<(HttpStatusCode Status, JsonNode? Body)> PackumentAsync(string repository, string name, string? token = null)
    {
        using var response = await SendAsync(HttpMethod.Get, NpmTestPackages.Path($"/r/{repository}/npm/", name), token: token);
        var text = await response.Content.ReadAsStringAsync(Cancel);

        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    private async Task<NpmPackageInfo?> StoredAsync(string repository, string name)
    {
        using var scope = await _app.CreateRepositoryScopeAsync(repository, Cancel);

        return await scope.ServiceProvider.GetRequiredService<NpmRegistryService>().GetPackageAsync(name, Cancel);
    }

    private static IEnumerable<string> Versions(JsonNode packument) => ((JsonObject)packument["versions"]!).Select(v => v.Key);

    // ---- merged packument ----

    [Fact]
    public async Task ThePackumentIsTheUnionOfTheMembersAndEachVersionComesFromTheFirstMemberThatHasIt()
    {
        var mine = await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "1.0.0", "2.0.0");

        var (status, packument) = await PackumentAsync("v", "pkg");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, Versions(packument!));
        // 1.0.0 is loc's (its integrity), 2.0.0 the upstream's; both are downloaded through the virtual repository.
        Assert.Equal(mine.Integrity, (string?)packument!["versions"]!["1.0.0"]!["dist"]!["integrity"]);
        Assert.Equal(_upstream.Tarball("pkg", "2.0.0").Integrity, (string?)packument["versions"]!["2.0.0"]!["dist"]!["integrity"]);
        Assert.Equal("http://localhost/r/v/npm/pkg/-/pkg-1.0.0.tgz", (string?)packument["versions"]!["1.0.0"]!["dist"]!["tarball"]);
        Assert.Equal("http://localhost/r/v/npm/pkg/-/pkg-2.0.0.tgz", (string?)packument["versions"]!["2.0.0"]!["dist"]!["tarball"]);
        Assert.Equal("pkg", (string?)packument["name"]);
        Assert.StartsWith("2-", (string?)packument["_rev"]);
    }

    [Fact]
    public async Task DistTagsFollowTheMemberOrderNotTheHighestVersion()
    {
        await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "1.0.0", "2.0.0");
        _upstream.Tag("pkg", "beta", "2.0.0");

        var (_, packument) = await PackumentAsync("v", "pkg");

        // loc has "latest" (1.0.0) and the upstream has "latest" (2.0.0) and "beta": the first member's latest wins, beta is added.
        Assert.Equal("1.0.0", (string?)packument!["dist-tags"]!["latest"]);
        Assert.Equal("2.0.0", (string?)packument["dist-tags"]!["beta"]);

        // The same members in the other order.
        await CreateVirtualAsync("w", new[] { "up", "loc" }, "loc");
        var (_, reversed) = await PackumentAsync("w", "pkg");
        Assert.Equal("2.0.0", (string?)reversed!["dist-tags"]!["latest"]);
        Assert.Equal(_upstream.Tarball("pkg", "1.0.0").Integrity, (string?)reversed["versions"]!["1.0.0"]!["dist"]!["integrity"]);
    }

    [Fact]
    public async Task TheTimeIsTheUnionWithTheEarliestCreatedAndTheLatestModified()
    {
        await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "2.0.0");

        var (_, packument) = await PackumentAsync("v", "pkg");

        var time = (JsonObject)packument!["time"]!;
        // loc's packument is dated by the test clock (2020-01-01), the upstream's by its own 2019 times.
        Assert.Equal("2019-01-01T00:00:00.000Z", (string?)time["created"]);
        Assert.Equal("2020-01-01T00:00:00.000Z", (string?)time["modified"]);
        Assert.NotNull(time["1.0.0"]);
        Assert.Equal("2019-02-01T10:00:00.000Z", (string?)time["2.0.0"]);
    }

    [Fact]
    public async Task AnInstallOnlyRequestGetsTheMergedInstallDocument()
    {
        await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "2.0.0");

        using var response = await SendAsync(HttpMethod.Get, "/r/v/npm/pkg", accept: "application/vnd.npm.install-v1+json");
        var packument = JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

        Assert.Equal("application/vnd.npm.install-v1+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, Versions(packument));
        Assert.Null(packument["readme"]);
        Assert.NotNull(packument["modified"]);
    }

    [Fact]
    public async Task APackageOnlyOneMemberHasIsServedAndOneNoMemberHasIsMissing()
    {
        _upstream.Add("remote-only", "3.0.0");
        await PublishAsync("loc", "local-only", "1.0.0");

        var (remote, _) = await PackumentAsync("v", "remote-only");
        var (local, _) = await PackumentAsync("v", "local-only");
        var (none, _) = await PackumentAsync("v", "nobody-has-this");

        Assert.Equal(HttpStatusCode.OK, remote);
        Assert.Equal(HttpStatusCode.OK, local);
        Assert.Equal(HttpStatusCode.NotFound, none);
    }

    [Fact]
    public async Task AScopedPackageIsMergedThroughTheEncodedSlash()
    {
        await PublishAsync("loc", "@acme/tool", "1.0.0");
        _upstream.Add("@acme/tool", "2.0.0");

        var (status, packument) = await PackumentAsync("v", "@acme/tool");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, Versions(packument!));
        Assert.Equal("http://localhost/r/v/npm/@acme/tool/-/tool-2.0.0.tgz", (string?)packument!["versions"]!["2.0.0"]!["dist"]!["tarball"]);
    }

    // ---- tarballs ----

    [Fact]
    public async Task ATarballComesFromTheFirstMemberThatHasItAndAnUpstreamOnlyOneIsStoredInTheRemoteMember()
    {
        var mine = await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "1.0.0", "2.0.0");

        using var first = await SendAsync(HttpMethod.Get, "/r/v/npm/pkg/-/pkg-1.0.0.tgz");
        Assert.Equal(mine.Bytes, await first.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(0, _upstream.TarballDownloads);

        using var second = await SendAsync(HttpMethod.Get, "/r/v/npm/pkg/-/pkg-2.0.0.tgz");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(_upstream.Tarball("pkg", "2.0.0").Bytes, await second.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(1, _upstream.TarballDownloads);

        // Stored by the remote member, not by loc; served from there with the upstream down.
        Assert.Equal("2.0.0", Assert.Single((await StoredAsync("up", "pkg"))!.Versions).Version);
        Assert.Equal("1.0.0", Assert.Single((await StoredAsync("loc", "pkg"))!.Versions).Version);
        _wire.Down = true;
        using var again = await SendAsync(HttpMethod.Get, "/r/v/npm/pkg/-/pkg-2.0.0.tgz");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task AVersionNoMemberHasIsNotFound()
    {
        await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "1.0.0");

        using var response = await SendAsync(HttpMethod.Get, "/r/v/npm/pkg/-/pkg-9.9.9.tgz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- writes ----

    [Fact]
    public async Task APublishToTheVirtualRepositoryIsStoredInItsDeployRepositoryOnly()
    {
        var tarball = NpmTestPackages.Tarball("fresh", "1.0.0");

        using var response = await SendAsync(HttpMethod.Put, "/r/v/npm/fresh", NpmTestPackages.PublishBody(tarball), Key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(await StoredAsync("loc", "fresh"));
        Assert.Null(await StoredAsync("up", "fresh"));
        var (status, packument) = await PackumentAsync("v", "fresh");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "1.0.0" }, Versions(packument!));
    }

    [Fact]
    public async Task APublishToAVirtualRepositoryWithoutADeployRepositoryIsRefused()
    {
        await CreateVirtualAsync("ro", new[] { "loc", "up" }, null);
        var tarball = NpmTestPackages.Tarball("fresh", "1.0.0");

        using var response = await SendAsync(HttpMethod.Put, "/r/ro/npm/fresh", NpmTestPackages.PublishBody(tarball), Key);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await StoredAsync("loc", "fresh"));
    }

    [Fact]
    public async Task DistTagsDeprecationAndUnpublishThroughTheVirtualRepositoryActInTheDeployRepository()
    {
        await PublishAsync("loc", "pkg", "1.0.0");
        await PublishAsync("loc", "pkg", "1.1.0");

        using var tag = await SendAsync(HttpMethod.Put, "/r/v/npm/-/package/pkg/dist-tags/beta", "\"1.1.0\"", Key);
        Assert.Equal(HttpStatusCode.OK, tag.StatusCode);
        Assert.Equal("1.1.0", (await StoredAsync("loc", "pkg"))!.DistTags["beta"]);

        var deprecate = new JsonObject
        {
            ["name"] = "pkg",
            ["versions"] = new JsonObject { ["1.0.0"] = new JsonObject { ["deprecated"] = "old" }, ["1.1.0"] = new JsonObject() },
        };
        using var deprecated = await SendAsync(HttpMethod.Put, "/r/v/npm/pkg", deprecate.ToJsonString(), Key);
        Assert.Equal(HttpStatusCode.OK, deprecated.StatusCode);
        Assert.Equal("old", (await StoredAsync("loc", "pkg"))!.Versions.Single(v => v.Version == "1.0.0").Deprecated);
        var (_, packument) = await PackumentAsync("v", "pkg");
        Assert.Equal("old", (string?)packument!["versions"]!["1.0.0"]!["deprecated"]);

        using var unpublished = await SendAsync(HttpMethod.Delete, "/r/v/npm/pkg/-rev/1-x", token: Key);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        Assert.Null(await StoredAsync("loc", "pkg"));
    }

    [Fact]
    public async Task APublishThroughTheVirtualRepositoryNeedsDeployInTheDeployRepositoryNotInTheVirtualOne()
    {
        // The deploy repository answers "who may deploy": here nobody holds Deploy on it without credentials.
        var tarball = NpmTestPackages.Tarball("fresh", "1.0.0");

        using var response = await SendAsync(HttpMethod.Put, "/r/v/npm/fresh", NpmTestPackages.PublishBody(tarball));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await StoredAsync("loc", "fresh"));
    }

    // ---- access ----

    [Fact]
    public async Task AMemberTheCallerCannotReadIsInvisibleThroughTheVirtualRepository()
    {
        await CreateRemoteAsync("secret-up", anonymousRead: false);
        await CreateVirtualAsync("mix", new[] { "loc", "secret-up" }, "loc");
        _upstream.Add("up-only", "1.0.0");
        var (anonymous, _) = await PackumentAsync("mix", "up-only");
        var (administrator, packument) = await PackumentAsync("mix", "up-only", Key);

        Assert.Equal(HttpStatusCode.NotFound, anonymous);
        Assert.Equal(HttpStatusCode.OK, administrator);
        Assert.NotNull(packument);
    }

    // ---- failing members, search, nesting ----

    [Fact]
    public async Task AMemberWhoseUpstreamIsDownIsSkippedAndTheOthersAnswer()
    {
        await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "2.0.0");
        _wire.Down = true;

        var (status, packument) = await PackumentAsync("v", "pkg");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "1.0.0" }, Versions(packument!));
        var (upstreamOnly, _) = await PackumentAsync("v", "elsewhere");
        Assert.Equal(HttpStatusCode.NotFound, upstreamOnly);
    }

    [Fact]
    public async Task SearchMergesTheMembersByNameAndLooksAtStoredPackagesOfARemoteMember()
    {
        await PublishAsync("loc", "left-pad", "1.0.0");
        await PublishAsync("loc", "apple-pad", "1.0.0");
        _upstream.Add("left-pad", "2.0.0");
        _upstream.Add("right-pad", "1.0.0");
        _upstream.Add("never-fetched-pad", "1.0.0");
        using (await SendAsync(HttpMethod.Get, "/r/up/npm/left-pad/-/left-pad-2.0.0.tgz")) { }
        using (await SendAsync(HttpMethod.Get, "/r/up/npm/right-pad/-/right-pad-1.0.0.tgz")) { }

        using var response = await SendAsync(HttpMethod.Get, "/r/v/npm/-/v1/search?text=pad&size=10");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;
        var names = ((JsonArray)body["objects"]!).Select(o => (string?)o!["package"]!["name"]).ToList();

        Assert.Equal(new[] { "apple-pad", "left-pad", "right-pad" }, names);
        Assert.Equal(3, (int)body["total"]!);

        using var paged = await SendAsync(HttpMethod.Get, "/r/v/npm/-/v1/search?text=pad&size=1&from=1");
        var page = JsonNode.Parse(await paged.Content.ReadAsStringAsync(Cancel))!;
        Assert.Equal("left-pad", (string?)page["objects"]![0]!["package"]!["name"]);
        Assert.Equal(3, (int)page["total"]!);
    }

    [Fact]
    public async Task AVirtualRepositoryOfAVirtualRepositoryReadsThroughBoth()
    {
        await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "2.0.0");
        await CreateVirtualAsync("outer", new[] { "v" }, null);

        var (status, packument) = await PackumentAsync("outer", "pkg");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, Versions(packument!));
    }

    // ---- administration ----

    [Fact]
    public async Task OnlyNpmRepositoriesCanBeMembersOfAnNpmVirtualRepositoryAndTheOtherWayRound()
    {
        await _app.CreateRepositoryAsync("nuget-local", Cancel);
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>();

        var candidates = await admin.CandidatesAsync("v", Cancel);
        Assert.DoesNotContain(candidates, c => c.Name == "nuget-local");
        Assert.All(candidates, c => Assert.Equal(RepositoryFormat.Npm, c.Format));

        var forNew = await admin.CandidatesAsync(null, Cancel, RepositoryFormat.Npm);
        Assert.Contains(forNew, c => c.Name == "loc");
        Assert.DoesNotContain(forNew, c => c.Name == "nuget-local");

        var e = await Assert.ThrowsAsync<ArgumentException>(() => admin.UpdateAsync("v", new VirtualRepositorySettings { Members = { "loc", "nuget-local" }, DeployRepository = "loc" }, Cancel));
        Assert.Contains("nuget-local", e.Message);
        Assert.Contains("Npm", e.Message);

        // A NuGet virtual repository does not take an npm member either.
        var nuget = await admin.CreateAsync("nv", new VirtualRepositorySettings { Members = { "nuget-local" } }, Cancel);
        Assert.Equal(RepositoryFormat.NuGet, nuget.Format);
        await Assert.ThrowsAsync<ArgumentException>(() => admin.UpdateAsync("nv", new VirtualRepositorySettings { Members = { "nuget-local", "loc" } }, Cancel));
    }

    [Fact]
    public async Task AVirtualNpmRepositoryIsCreatedWithItsFormatAndAKnownDeployRepositoryIsRequired()
    {
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>();

        var detail = await admin.FindAsync("v", Cancel);
        Assert.Equal(RepositoryFormat.Npm, detail!.Repository.Format);
        Assert.Equal("loc", detail.DeployTarget!.Name);
        Assert.Equal(new[] { "loc", "up" }, detail.EffectiveMembers.Select(m => m.Name));

        // A remote repository takes no deployments.
        await Assert.ThrowsAsync<ArgumentException>(() => admin.CreateAsync(
            "bad", new VirtualRepositorySettings { Members = { "loc", "up" }, DeployRepository = "up" }, Cancel, format: RepositoryFormat.Npm));
    }

    [Fact]
    public async Task ThePreviewTellsWhereAnNpmVersionWouldComeFrom()
    {
        await PublishAsync("loc", "pkg", "1.0.0");
        _upstream.Add("pkg", "1.0.0", "2.0.0");

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>();

        var mine = await admin.PreviewAsync("v", "pkg", NuGetVersion.Parse("1.0.0"), Cancel);
        Assert.Equal("loc", mine.Answers!.Name);
        Assert.Equal(new[] { ResolutionOutcome.Stored, ResolutionOutcome.NotAsked }, mine.Steps.Select(s => s.Outcome));

        var theirs = await admin.PreviewAsync("v", "pkg", NuGetVersion.Parse("2.0.0"), Cancel);
        Assert.Equal("up", theirs.Answers!.Name);
        Assert.Equal(new[] { ResolutionOutcome.NotFound, ResolutionOutcome.WouldFetch }, theirs.Steps.Select(s => s.Outcome));
        Assert.Equal(0, _upstream.TarballDownloads);
        Assert.Null(await StoredAsync("up", "pkg"));

        var nobody = await admin.PreviewAsync("v", "pkg", NuGetVersion.Parse("9.0.0"), Cancel);
        Assert.Null(nobody.Answers);

        _wire.Down = true;
        using var fresh = _app.Services.CreateScope();
        var down = await fresh.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().PreviewAsync("v", "other", NuGetVersion.Parse("1.0.0"), Cancel);
        Assert.Equal(ResolutionOutcome.Unavailable, down.Steps[1].Outcome);
    }

    [Fact]
    public async Task ThePreviewOfAnOfflineRemoteMemberSaysItIsNotAsked()
    {
        _upstream.Add("pkg", "2.0.0");
        using var scope = _app.Services.CreateScope();
        var remotes = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var settings = (await remotes.FindAsync("up", Cancel))!.Settings;
        settings.Offline = true;
        await remotes.UpdateAsync("up", settings, clearPassword: false, Cancel);

        var preview = await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().PreviewAsync("v", "pkg", NuGetVersion.Parse("2.0.0"), Cancel);

        Assert.Equal(ResolutionOutcome.Offline, preview.Steps[1].Outcome);
        Assert.Empty(_upstream.Requests);
    }
}
