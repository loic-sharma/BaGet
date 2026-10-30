using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The npm registry over HTTP (P3): publish with the tarball inline, packument, tarball, dist-tags, deprecate, unpublish, search,
/// ping, whoami and the write-access rules of a server with a global API key. The test server hands the path over decoded, so a
/// scoped name arrives as <c>@scope/name</c>; the real client (<c>BaGet.Clients.Tests</c>) sends <c>@scope%2fname</c> to Kestrel.
/// </summary>
public class NpmRegistryTests : IDisposable
{
    private const string Key = "secret-key";
    private const string Registry = "/r/web/npm/";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    private readonly ITestOutputHelper _output;

    public NpmRegistryTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "ApiKey", Key } });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _app.CreateNpmRepositoryAsync("web").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private static string Path(string name) => NpmTestPackages.Path(Registry, name);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string? body = null, string? token = Key, string? accept = null)
    {
        using var request = NpmTestPackages.Json(method, uri, body, token);
        if (accept != null) request.Headers.Accept.ParseAdd(accept);

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<JsonNode?> JsonAsync(HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private async Task<NpmTarballFile> PublishAsync(string name, string version, string tag = "latest", string registry = Registry)
    {
        var tarball = NpmTestPackages.Tarball(name, version);
        using var response = await SendAsync(HttpMethod.Put, NpmTestPackages.Path(registry, name), NpmTestPackages.PublishBody(tarball, tag));
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");

        return tarball;
    }

    // ---- publish and read ----

    [Fact]
    public async Task APublishedPackageIsServedAsAPackumentWithItsTarballAddress()
    {
        var tarball = await PublishAsync("left-pad", "1.0.0");

        using var response = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var packument = (await JsonAsync(response))!;

        Assert.Equal("left-pad", (string?)packument["name"]);
        Assert.Equal("1.0.0", (string?)packument["dist-tags"]!["latest"]);
        var version = packument["versions"]!["1.0.0"]!;
        Assert.Equal("left-pad@1.0.0", (string?)version["_id"]);
        Assert.Equal("http://localhost/r/web/npm/left-pad/-/left-pad-1.0.0.tgz", (string?)version["dist"]!["tarball"]);
        Assert.Equal(tarball.Integrity, (string?)version["dist"]!["integrity"]);
        Assert.Equal(tarball.Shasum, (string?)version["dist"]!["shasum"]);
        Assert.Equal("A test package", (string?)packument["description"]);
        Assert.Equal("# Readme", (string?)packument["readme"]);
        Assert.StartsWith("1-", (string?)packument["_rev"]);
        Assert.NotNull(packument["time"]!["1.0.0"]);
        Assert.Equal("2020-01-01T00:00:00.000Z", (string?)packument["time"]!["created"]);
    }

    [Fact]
    public async Task TheTarballIsServedByteForByte()
    {
        var tarball = await PublishAsync("left-pad", "1.0.0");

        using var response = await SendAsync(HttpMethod.Get, Registry + "left-pad/-/left-pad-1.0.0.tgz", token: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(tarball.Bytes, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("\"" + tarball.Integrity + "\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task AScopedPackageIsPublishedAndServedUnderItsScopedPath()
    {
        var tarball = await PublishAsync("@acme/tool", "2.0.0");

        using var packument = await SendAsync(HttpMethod.Get, Registry + "@acme/tool", token: null);
        using var file = await SendAsync(HttpMethod.Get, Registry + "@acme/tool/-/tool-2.0.0.tgz", token: null);

        Assert.Equal(HttpStatusCode.OK, packument.StatusCode);
        var document = (await JsonAsync(packument))!;
        Assert.Equal("@acme/tool", (string?)document["name"]);
        Assert.Equal("http://localhost/r/web/npm/@acme/tool/-/tool-2.0.0.tgz", (string?)document["versions"]!["2.0.0"]!["dist"]!["tarball"]);
        Assert.Equal(tarball.Bytes, await file.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishingTheSameVersionAgainIsRefusedWith409AndNothingChanges()
    {
        var first = await PublishAsync("left-pad", "1.0.0");
        var other = NpmTestPackages.Tarball("left-pad", "1.0.0", description: "different");

        using var response = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(other));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("1.0.0", (string?)(await JsonAsync(response))!["error"]);
        using var tarball = await SendAsync(HttpMethod.Get, Registry + "left-pad/-/left-pad-1.0.0.tgz", token: null);
        Assert.Equal(first.Bytes, await tarball.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABuildMetadataVariantOfAPublishedVersionIsTheSameVersion()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var response = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(NpmTestPackages.Tarball("left-pad", "1.0.0+build")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ATarballThatDoesNotMatchTheDeclaredIntegrityIsRefusedAndNotStored()
    {
        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0");

        using var wrongIntegrity = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(tarball, integrity: "sha512-AAAA"));
        using var wrongShasum = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(tarball, shasum: new string('0', 40)));

        Assert.Equal(HttpStatusCode.BadRequest, wrongIntegrity.StatusCode);
        Assert.Contains("integrity", (string?)(await JsonAsync(wrongIntegrity))!["error"]);
        Assert.Equal(HttpStatusCode.BadRequest, wrongShasum.StatusCode);
        using var packument = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        using var file = await SendAsync(HttpMethod.Get, Registry + "left-pad/-/left-pad-1.0.0.tgz", token: null);
        Assert.Equal(HttpStatusCode.NotFound, packument.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
    }

    [Fact]
    public async Task ATarballWhosePackageJsonIsAnotherPackageIsRefused()
    {
        var wrongName = NpmTestPackages.Tarball("left-pad", "1.0.0", packageJsonName: "right-pad");
        var wrongVersion = NpmTestPackages.Tarball("left-pad", "1.0.0", packageJsonVersion: "9.9.9");

        using var byName = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(wrongName));
        using var byVersion = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(wrongVersion));

        Assert.Equal(HttpStatusCode.BadRequest, byName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, byVersion.StatusCode);
        Assert.Contains("right-pad", (string?)(await JsonAsync(byName))!["error"]);
    }

    [Fact]
    public async Task ABodyThatIsNotATarballIsRefused()
    {
        var notATarball = new NpmTarballFile("left-pad", "1.0.0", Encoding.UTF8.GetBytes("plain text"), "x", "y");
        var body = NpmTestPackages.PublishBody(notATarball, integrity: "sha512-" + Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(notATarball.Bytes)), shasum: Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(notATarball.Bytes)).ToLowerInvariant());

        using var response = await SendAsync(HttpMethod.Put, Path("left-pad"), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("tarball", (string?)(await JsonAsync(response))!["error"]);
    }

    [Theory]
    [InlineData("Left-Pad")]
    [InlineData("_hidden")]
    [InlineData(".dot")]
    [InlineData("node_modules")]
    public async Task InvalidPackageNamesAreRefused(string name)
    {
        var tarball = NpmTestPackages.Tarball(name, "1.0.0");

        using var response = await SendAsync(HttpMethod.Put, Registry + name, NpmTestPackages.PublishBody(tarball));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ANonSemanticVersionIsRefused()
    {
        using var response = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(NpmTestPackages.Tarball("left-pad", "1.0")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task APublishWithoutATagOrWithAVersionAsTagIsRefused()
    {
        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0");
        var noTag = JsonNode.Parse(NpmTestPackages.PublishBody(tarball))!.AsObject();
        noTag["dist-tags"] = new JsonObject();

        using var withoutTag = await SendAsync(HttpMethod.Put, Path("left-pad"), noTag.ToJsonString());
        using var versionTag = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(tarball, tag: "2.0.0"));

        Assert.Equal(HttpStatusCode.BadRequest, withoutTag.StatusCode);
        Assert.Contains("--tag", (string?)(await JsonAsync(withoutTag))!["error"]);
        Assert.Equal(HttpStatusCode.BadRequest, versionTag.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNotJsonIsRefused()
    {
        using var response = await SendAsync(HttpMethod.Put, Path("left-pad"), "{not json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ARequestOverTheConfiguredLimitIsRefusedWith413()
    {
        using var app = new BaGetApplication(
            TestOutput,
            configOverrides: new Dictionary<string, string> { { "ApiKey", Key }, { "Npm:MaxPublishBytes", "4096" } });
        await app.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);
        using var client = app.CreateClient();
        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0", readme: new string('x', 20_000));

        using var request = NpmTestPackages.Json(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(tarball, readme: new string('x', 20_000)), Key);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var packument = await client.GetAsync(Path("left-pad"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, packument.StatusCode);
    }

    private ITestOutputHelper TestOutput => _output;

    // ---- authentication ----

    [Fact]
    public async Task WritesNeedTheApiKeyAsABearerTokenOrBasicPassword()
    {
        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0");
        var body = NpmTestPackages.PublishBody(tarball);

        using var anonymous = await SendAsync(HttpMethod.Put, Path("left-pad"), body, token: null);
        using var wrong = await SendAsync(HttpMethod.Put, Path("left-pad"), body, token: "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains(anonymous.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");

        using var basic = new HttpRequestMessage(HttpMethod.Put, Path("left-pad")) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        basic.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("anyone:" + Key)));
        using var viaBasic = await _client.SendAsync(basic, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, viaBasic.StatusCode);

        using var packument = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        Assert.Equal(HttpStatusCode.OK, packument.StatusCode);
    }

    [Fact]
    public async Task AServerWithoutAnyKeyAcceptsPublishesAsItAlwaysDid()
    {
        using var app = new BaGetApplication(TestOutput);
        await app.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);
        using var client = app.CreateClient();
        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0");

        using var request = NpmTestPackages.Json(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(tarball));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyModeRefusesEveryWrite()
    {
        using var app = new BaGetApplication(TestOutput, configOverrides: new Dictionary<string, string> { { "ApiKey", Key }, { "IsReadOnlyMode", "true" } });
        await app.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);
        using var client = app.CreateClient();
        var tarball = NpmTestPackages.Tarball("left-pad", "1.0.0");

        using var request = NpmTestPackages.Json(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(tarball), Key);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task APublishWhileTheMaintenanceGateIsClosedIs503AndLeavesNoFile()
    {
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            using var response = await SendAsync(HttpMethod.Put, Path("left-pad"), NpmTestPackages.PublishBody(NpmTestPackages.Tarball("left-pad", "1.0.0")));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            gate.Open();
        }

        using var file = await SendAsync(HttpMethod.Get, Registry + "left-pad/-/left-pad-1.0.0.tgz", token: null);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        using var scope = _app.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        await Assert.ThrowsAnyAsync<IOException>(async () => (await storage.GetAsync("repos/web/npm/left-pad/-/left-pad-1.0.0.tgz", TestContext.Current.CancellationToken)).Dispose());
    }

    // ---- read variants ----

    [Fact]
    public async Task AnUnknownPackageOrFileIs404WithAnErrorBody()
    {
        using var packument = await SendAsync(HttpMethod.Get, Path("missing"), token: null);
        await PublishAsync("left-pad", "1.0.0");
        using var file = await SendAsync(HttpMethod.Get, Registry + "left-pad/-/left-pad-9.9.9.tgz", token: null);
        using var foreign = await SendAsync(HttpMethod.Get, Registry + "left-pad/-/other-1.0.0.tgz", token: null);

        Assert.Equal(HttpStatusCode.NotFound, packument.StatusCode);
        Assert.NotNull((await JsonAsync(packument))!["error"]);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task TheInstallOnlyDocumentIsServedWhenTheClientAsksForIt()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var response = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null, accept: "application/vnd.npm.install-v1+json; q=1.0, application/json; q=0.8, */*");

        Assert.Equal("application/vnd.npm.install-v1+json", response.Content.Headers.ContentType?.MediaType);
        var document = (await JsonAsync(response))!;
        Assert.Equal("left-pad", (string?)document["name"]);
        Assert.NotNull(document["modified"]);
        Assert.Equal("1.0.0", (string?)document["dist-tags"]!["latest"]);
        var version = document["versions"]!["1.0.0"]!.AsObject();
        Assert.Contains("dist", version.Select(p => p.Key));
        Assert.DoesNotContain("description", version.Select(p => p.Key));
        Assert.Null(document["readme"]);
    }

    [Fact]
    public async Task SearchFindsPackagesByNameAndPages()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("right-pad", "1.0.0");
        await PublishAsync("@acme/other", "1.0.0");

        using var all = await SendAsync(HttpMethod.Get, Registry + "-/v1/search?text=pad&size=1&from=0", token: null);
        using var second = await SendAsync(HttpMethod.Get, Registry + "-/v1/search?text=pad&size=1&from=1", token: null);
        using var scoped = await SendAsync(HttpMethod.Get, Registry + "-/v1/search?text=acme", token: null);

        var first = (await JsonAsync(all))!;
        Assert.Equal(2, (int?)first["total"]);
        Assert.Equal("left-pad", (string?)first["objects"]![0]!["package"]!["name"]);
        Assert.Equal("1.0.0", (string?)first["objects"]![0]!["package"]!["version"]);
        Assert.Equal("A test package", (string?)first["objects"]![0]!["package"]!["description"]);
        Assert.Equal("right-pad", (string?)(await JsonAsync(second))!["objects"]![0]!["package"]!["name"]);
        var scopedHit = (await JsonAsync(scoped))!["objects"]![0]!["package"]!;
        Assert.Equal("@acme/other", (string?)scopedHit["name"]);
        Assert.Equal("acme", (string?)scopedHit["scope"]);
    }

    [Fact]
    public async Task PingAnswersAnEmptyObjectAndTheAuditCallsAnswerNoAdvisories()
    {
        using var ping = await SendAsync(HttpMethod.Get, Registry + "-/ping", token: null);
        using var bulk = await SendAsync(HttpMethod.Post, Registry + "-/npm/v1/security/advisories/bulk", "{}", token: null);
        using var quick = await SendAsync(HttpMethod.Post, Registry + "-/npm/v1/security/audits/quick", "{}", token: null);

        Assert.Equal("{}", (await JsonAsync(ping))!.ToJsonString());
        Assert.Equal("{}", (await JsonAsync(bulk))!.ToJsonString());
        Assert.Empty((await JsonAsync(quick))!["advisories"]!.AsObject());
    }

    [Fact]
    public async Task WhoamiNamesTheCallerAndNeedsCredentials()
    {
        using var named = await SendAsync(HttpMethod.Get, Registry + "-/whoami");
        using var anonymous = await SendAsync(HttpMethod.Get, Registry + "-/whoami", token: null);

        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal("superuser", (string?)(await JsonAsync(named))!["username"]);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task TheWebLoginIsNotOfferedSoTheClientFallsBack()
    {
        using var response = await SendAsync(HttpMethod.Post, Registry + "-/v1/login", "{}", token: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LoggingInWithoutAccountsExplainsToUseTheApiKey()
    {
        using var response = await SendAsync(HttpMethod.Put, Registry + "-/user/org.couchdb.user:alice", """{"name":"alice","password":"pw"}""", token: null);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("API key", (string?)(await JsonAsync(response))!["error"]);
    }

    // ---- dist-tags ----

    [Fact]
    public async Task DistTagsCanBeListedSetAndRemoved()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "2.0.0-beta.1", tag: "next");

        using var list = await SendAsync(HttpMethod.Get, Registry + "-/package/left-pad/dist-tags", token: null);
        var tags = (await JsonAsync(list))!;
        Assert.Equal("1.0.0", (string?)tags["latest"]);
        Assert.Equal("2.0.0-beta.1", (string?)tags["next"]);

        using var set = await SendAsync(HttpMethod.Put, Registry + "-/package/left-pad/dist-tags/stable", "\"1.0.0\"");
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        using var after = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        Assert.Equal("1.0.0", (string?)(await JsonAsync(after))!["dist-tags"]!["stable"]);

        using var remove = await SendAsync(HttpMethod.Delete, Registry + "-/package/left-pad/dist-tags/next");
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        using var missing = await SendAsync(HttpMethod.Delete, Registry + "-/package/left-pad/dist-tags/next");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var final = await SendAsync(HttpMethod.Get, Registry + "-/package/left-pad/dist-tags", token: null);
        Assert.Null((await JsonAsync(final))!["next"]);
    }

    [Fact]
    public async Task ATagCannotPointAtAMissingVersionNorBeNamedLikeOne()
    {
        await PublishAsync("left-pad", "1.0.0");

        using var missing = await SendAsync(HttpMethod.Put, Registry + "-/package/left-pad/dist-tags/beta", "\"9.9.9\"");
        using var versionLike = await SendAsync(HttpMethod.Put, Registry + "-/package/left-pad/dist-tags/2.0.0", "\"1.0.0\"");
        using var notAString = await SendAsync(HttpMethod.Put, Registry + "-/package/left-pad/dist-tags/beta", "{}");
        using var unknownPackage = await SendAsync(HttpMethod.Put, Registry + "-/package/nothing/dist-tags/beta", "\"1.0.0\"");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, versionLike.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notAString.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownPackage.StatusCode);
    }

    [Fact]
    public async Task ATagMovedByATaggedPublishRepointsAndTheFirstVersionIsAlwaysLatest()
    {
        await PublishAsync("left-pad", "1.0.0", tag: "beta");
        using var first = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        var tags = (await JsonAsync(first))!["dist-tags"]!;
        Assert.Equal("1.0.0", (string?)tags["latest"]);
        Assert.Equal("1.0.0", (string?)tags["beta"]);

        await PublishAsync("left-pad", "1.1.0", tag: "beta");
        using var second = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        tags = (await JsonAsync(second))!["dist-tags"]!;
        Assert.Equal("1.0.0", (string?)tags["latest"]);
        Assert.Equal("1.1.0", (string?)tags["beta"]);
    }

    // ---- deprecate ----

    [Fact]
    public async Task DeprecatingWritesTheMessageOnTheVersionAndClearingRemovesIt()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "1.1.0");
        using var current = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        var packument = (await JsonAsync(current))!.AsObject();
        packument["versions"]!["1.0.0"]!["deprecated"] = "use 1.1.0";

        using var deprecate = await SendAsync(HttpMethod.Put, Path("left-pad"), packument.ToJsonString());
        Assert.Equal(HttpStatusCode.OK, deprecate.StatusCode);

        using var after = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        var deprecated = (await JsonAsync(after))!.AsObject();
        Assert.Equal("use 1.1.0", (string?)deprecated["versions"]!["1.0.0"]!["deprecated"]);
        Assert.Null(deprecated["versions"]!["1.1.0"]!["deprecated"]);

        deprecated["versions"]!["1.0.0"]!["deprecated"] = "";
        using var clear = await SendAsync(HttpMethod.Put, Path("left-pad"), deprecated.ToJsonString());
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        using var cleared = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        Assert.Null((await JsonAsync(cleared))!["versions"]!["1.0.0"]!["deprecated"]);
    }

    [Fact]
    public async Task APackumentPutCannotAddAVersionWithoutATarball()
    {
        await PublishAsync("left-pad", "1.0.0");
        using var current = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        var packument = (await JsonAsync(current))!.AsObject();
        packument["versions"]!["5.0.0"] = new JsonObject { ["name"] = "left-pad", ["version"] = "5.0.0" };

        using var response = await SendAsync(HttpMethod.Put, Path("left-pad"), packument.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- unpublish ----

    [Fact]
    public async Task UnpublishingAVersionTakesThePutThenTheTarballDeleteAndRepointsLatest()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "1.1.0");
        using var current = await SendAsync(HttpMethod.Get, Path("left-pad") + "?write=true", token: null);
        var packument = (await JsonAsync(current))!.AsObject();
        var rev = (string)packument["_rev"]!;

        // What npm sends: the packument without the version and with the tags recomputed.
        packument["versions"]!.AsObject().Remove("1.1.0");
        packument["dist-tags"]!["latest"] = "1.0.0";
        using var put = await SendAsync(HttpMethod.Put, Path("left-pad") + "/-rev/" + rev, packument.ToJsonString());
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var delete = await SendAsync(HttpMethod.Delete, Registry + "left-pad/-/left-pad-1.1.0.tgz/-rev/" + rev);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var after = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        var document = (await JsonAsync(after))!;
        Assert.Equal("1.0.0", (string?)document["dist-tags"]!["latest"]);
        Assert.Null(document["versions"]!["1.1.0"]);
        using var file = await SendAsync(HttpMethod.Get, Registry + "left-pad/-/left-pad-1.1.0.tgz", token: null);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        await AssertNoStoredFileAsync("left-pad", "1.1.0");
    }

    [Fact]
    public async Task UnpublishingTheLastVersionRemovesThePackage()
    {
        await PublishAsync("left-pad", "1.0.0");
        using var current = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        var packument = (await JsonAsync(current))!.AsObject();
        packument["versions"]!.AsObject().Remove("1.0.0");

        using var put = await SendAsync(HttpMethod.Put, Path("left-pad") + "/-rev/" + (string)packument["_rev"]!, packument.ToJsonString());
        using var after = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        await AssertNoStoredFileAsync("left-pad", "1.0.0");
    }

    [Fact]
    public async Task UnpublishingAWholePackageDeletesEveryVersionAndFile()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "1.1.0");

        using var delete = await SendAsync(HttpMethod.Delete, Path("left-pad") + "/-rev/1-abc");
        using var after = await SendAsync(HttpMethod.Get, Path("left-pad"), token: null);
        using var again = await SendAsync(HttpMethod.Delete, Path("left-pad") + "/-rev/1-abc");

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        await AssertNoStoredFileAsync("left-pad", "1.0.0");
        await AssertNoStoredFileAsync("left-pad", "1.1.0");
    }

    [Fact]
    public async Task APublishedVersionCanBePublishedAgainAfterItWasUnpublished()
    {
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "1.1.0");
        using var delete = await SendAsync(HttpMethod.Delete, Registry + "left-pad/-/left-pad-1.1.0.tgz/-rev/1-abc");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        await PublishAsync("left-pad", "1.1.0");

        using var file = await SendAsync(HttpMethod.Get, Registry + "left-pad/-/left-pad-1.1.0.tgz", token: null);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
    }

    private async Task AssertNoStoredFileAsync(string name, string version)
    {
        using var scope = _app.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        await Assert.ThrowsAnyAsync<IOException>(async () =>
            (await storage.GetAsync($"repos/web/npm/{name}/-/{NpmNames.TarballFileName(name, version)}", TestContext.Current.CancellationToken)).Dispose());
    }

    // ---- format and repository separation ----

    [Fact]
    public async Task NpmRoutesExistOnlyInNpmRepositoriesAndNuGetRoutesNotThere()
    {
        await _app.CreateRepositoryAsync("feed", TestContext.Current.CancellationToken);
        await PublishAsync("left-pad", "1.0.0");

        using var inNuGet = await SendAsync(HttpMethod.Get, "/r/feed/npm/left-pad", token: null);
        using var inDefault = await SendAsync(HttpMethod.Get, "/npm/left-pad", token: null);
        using var nugetRouteInNpm = await SendAsync(HttpMethod.Get, "/r/web/v3/index.json", token: null);
        using var unknown = await SendAsync(HttpMethod.Get, "/r/nope/npm/left-pad", token: null);

        Assert.Equal(HttpStatusCode.NotFound, inNuGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inDefault.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nugetRouteInNpm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task TheSameNameInTwoNpmRepositoriesIsIndependent()
    {
        await _app.CreateNpmRepositoryAsync("other", cancellationToken: TestContext.Current.CancellationToken);
        await PublishAsync("left-pad", "1.0.0");
        await PublishAsync("left-pad", "7.0.0", registry: "/r/other/npm/");

        using var web = await SendAsync(HttpMethod.Get, Registry + "left-pad", token: null);
        using var other = await SendAsync(HttpMethod.Get, "/r/other/npm/left-pad", token: null);

        Assert.Equal("1.0.0", (string?)(await JsonAsync(web))!["dist-tags"]!["latest"]);
        Assert.Equal("7.0.0", (string?)(await JsonAsync(other))!["dist-tags"]!["latest"]);
        using var wrongFile = await SendAsync(HttpMethod.Get, "/r/other/npm/left-pad/-/left-pad-1.0.0.tgz", token: null);
        Assert.Equal(HttpStatusCode.NotFound, wrongFile.StatusCode);
        Assert.Contains("http://localhost/r/other/npm/left-pad/-/left-pad-7.0.0.tgz", await other.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EveryNpmEndpointCarriesTheNpmFormatMarker()
    {
        var endpoints = _app.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("npm/", StringComparison.Ordinal) || e.RoutePattern.RawText!.StartsWith("r/{repo}/npm/", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, e => Assert.Equal(RepositoryFormat.Npm, e.Metadata.GetMetadata<BaGet.Web.V3.Routing.RepositoryRouteMetadata>()?.Format));
    }

    [Fact]
    public async Task TheFirstPublisherOfADistTagKeepsTheStoredIntegrityOfTheTarballItSent()
    {
        var tarball = await PublishAsync("left-pad", "1.0.0");

        using var scope = _app.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use((await registry.FindAsync("web", TestContext.Current.CancellationToken))!);
        var package = await scope.ServiceProvider.GetRequiredService<INpmRegistry>().GetPackageAsync("left-pad", TestContext.Current.CancellationToken);

        Assert.Equal(tarball.Integrity, package!.Versions.Single().Integrity);
        Assert.Equal(tarball.Bytes.Length, package.Versions.Single().TarballSize);
        Assert.False(package.Versions.Single().Manifest["dist"]!.AsObject().ContainsKey("tarball"));
    }
}
