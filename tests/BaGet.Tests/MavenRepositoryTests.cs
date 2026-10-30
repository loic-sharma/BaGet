using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The Maven repository protocol over HTTP (P4): deploy of files with their checksums and metadata, generated
/// <c>maven-metadata.xml</c>, snapshots with timestamped builds, the release/snapshot policy, redeploy, deletes, and the
/// authentication Maven uses (Basic, after a challenge). The real client is exercised in <c>BaGet.Clients.Tests</c>.
/// </summary>
public class MavenRepositoryTests : IDisposable
{
    private const string Key = "secret-key";
    private const string Repo = "/r/libs/maven/";
    private const string Jar = "org/acme/lib/1.0/lib-1.0.jar";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public MavenRepositoryTests(ITestOutputHelper output)
        : this(output, new Dictionary<string, string>())
    {
    }

    private MavenRepositoryTests(ITestOutputHelper output, Dictionary<string, string> extra)
    {
        var configuration = new Dictionary<string, string> { { "ApiKey", Key } };
        foreach (var pair in extra) configuration[pair.Key] = pair.Value;

        _app = new BaGetApplication(output, configOverrides: configuration);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, byte[]? body = null, string? key = Key, string repo = Repo)
    {
        using var request = new HttpRequestMessage(method, repo + path);
        if (body != null) request.Content = new ByteArrayContent(body);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Bytes("deployer:" + key)));

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private async Task PutOkAsync(string path, byte[] body, string repo = Repo)
    {
        using var response = await SendAsync(HttpMethod.Put, path, body, repo: repo);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"PUT {path}: {response.StatusCode} {await TextAsync(response)}");
    }

    private async Task<string> GetTextAsync(string path, string? key = Key, string repo = Repo)
    {
        using var response = await SendAsync(HttpMethod.Get, path, key: key, repo: repo);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {response.StatusCode}");

        return await TextAsync(response);
    }

    private Task CreateAsync(string? policy = null, bool redeploy = false, int snapshotBuilds = 0, bool anonymousRead = true)
        => _app.CreateMavenRepositoryAsync("libs", policy, redeploy, snapshotBuilds, anonymousRead, TestContext.Current.CancellationToken);

    private static string PomFor(string artifact, string version, string name = "A library") => MavenPom.Generate("org.acme", artifact, version, "jar", name, "Does things");

    // ---- release deploy and read ----

    [Fact]
    public async Task ADeployedFileIsServedByteForByteWithItsChecksums()
    {
        await CreateAsync();
        var content = Bytes("jar bytes");

        await PutOkAsync(Jar, content);

        using var response = await SendAsync(HttpMethod.Get, Jar, key: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("application/java-archive", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Hex(SHA1.HashData(content)), response.Headers.GetValues("X-Checksum-Sha1").Single());
        Assert.Equal(Hex(MD5.HashData(content)), response.Headers.GetValues("X-Checksum-Md5").Single());
        Assert.Equal("\"" + Hex(SHA1.HashData(content)) + "\"", response.Headers.ETag?.Tag);
    }

    [Theory]
    [InlineData("sha1")]
    [InlineData("md5")]
    [InlineData("sha256")]
    [InlineData("sha512")]
    public async Task EveryChecksumOfAStoredFileIsServedAsText(string algorithm)
    {
        await CreateAsync();
        var content = Bytes("jar bytes");
        await PutOkAsync(Jar, content);

        var expected = algorithm switch
        {
            "sha1" => Hex(SHA1.HashData(content)),
            "md5" => Hex(MD5.HashData(content)),
            "sha256" => Hex(SHA256.HashData(content)),
            _ => Hex(SHA512.HashData(content)),
        };

        Assert.Equal(expected, await GetTextAsync($"{Jar}.{algorithm}", key: null));
    }

    [Fact]
    public async Task AHeadRequestAnswersTheSizeAndNoBody()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("0123456789"));

        using var response = await SendAsync(HttpMethod.Head, Jar, key: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMissingFileAndAMissingChecksumAre404()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("x"));

        using var file = await SendAsync(HttpMethod.Get, "org/acme/lib/1.0/lib-1.0-sources.jar", key: null);
        using var version = await SendAsync(HttpMethod.Get, "org/acme/lib/2.0/lib-2.0.jar", key: null);
        using var artifact = await SendAsync(HttpMethod.Get, "org/acme/none/1.0/none-1.0.jar", key: null);
        using var checksum = await SendAsync(HttpMethod.Get, "org/acme/lib/1.0/lib-1.0-sources.jar.sha1", key: null);

        Assert.All(new[] { file, version, artifact, checksum }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    [Fact]
    public async Task AChecksumTheClientSendsMustMatchTheFileAndIsNotStored()
    {
        await CreateAsync();
        var content = Bytes("jar bytes");
        await PutOkAsync(Jar, content);

        using var good = await SendAsync(HttpMethod.Put, Jar + ".sha1", Bytes(Hex(SHA1.HashData(content))));
        using var bad = await SendAsync(HttpMethod.Put, Jar + ".sha1", Bytes(new string('0', 40)));

        Assert.Equal(HttpStatusCode.Created, good.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("does not match", await TextAsync(bad));
        Assert.Equal(Hex(SHA1.HashData(content)), await GetTextAsync(Jar + ".sha1"));
    }

    [Fact]
    public async Task AChecksumFileMayCarryTheFileNameAfterTheHash()
    {
        await CreateAsync();
        var content = Bytes("jar bytes");
        await PutOkAsync(Jar, content);

        using var response = await SendAsync(HttpMethod.Put, Jar + ".md5", Bytes(Hex(MD5.HashData(content)) + "  lib-1.0.jar\n"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- metadata ----

    [Fact]
    public async Task ArtifactMetadataIsGeneratedFromTheStoredVersions()
    {
        await CreateAsync(policy: "Both");
        await PutOkAsync("org/acme/lib/1.0/lib-1.0.jar", Bytes("1"));
        await PutOkAsync("org/acme/lib/1.1/lib-1.1.jar", Bytes("2"));
        await PutOkAsync("org/acme/lib/1.2-SNAPSHOT/lib-1.2-20240101.100000-1.jar", Bytes("3"));

        var xml = XDocument.Parse(await GetTextAsync("org/acme/lib/maven-metadata.xml", key: null));

        var versioning = xml.Root!.Element("versioning")!;
        Assert.Equal("org.acme", xml.Root.Element("groupId")!.Value);
        Assert.Equal("1.1", versioning.Element("release")!.Value);
        Assert.Equal("1.2-SNAPSHOT", versioning.Element("latest")!.Value);
        Assert.Equal(new[] { "1.0", "1.1", "1.2-SNAPSHOT" }, versioning.Element("versions")!.Elements("version").Select(v => v.Value));
    }

    [Fact]
    public async Task MetadataChecksumsAreOfTheGeneratedBytes()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("x"));

        using var metadata = await SendAsync(HttpMethod.Get, "org/acme/lib/maven-metadata.xml", key: null);
        var bytes = await metadata.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Hex(SHA1.HashData(bytes)), await GetTextAsync("org/acme/lib/maven-metadata.xml.sha1", key: null));
        Assert.Equal(Hex(MD5.HashData(bytes)), await GetTextAsync("org/acme/lib/maven-metadata.xml.md5", key: null));
    }

    [Fact]
    public async Task AMetadataFileTheClientUploadsIsAcknowledgedAndNotStored()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("x"));

        await PutOkAsync("org/acme/lib/maven-metadata.xml", Bytes("<metadata><versioning><latest>99</latest></versioning></metadata>"));
        await PutOkAsync("org/acme/lib/maven-metadata.xml.sha1", Bytes("0000"));

        var xml = XDocument.Parse(await GetTextAsync("org/acme/lib/maven-metadata.xml", key: null));
        Assert.Equal("1.0", xml.Root!.Element("versioning")!.Element("latest")!.Value);
    }

    [Fact]
    public async Task MetadataOfAnUnknownArtifactIs404()
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Get, "org/acme/none/maven-metadata.xml", key: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- snapshots ----

    [Fact]
    public async Task SnapshotBuildsAreResolvedByTheVersionMetadataAndTheLiteralSnapshotName()
    {
        await CreateAsync(policy: "Snapshots");
        var dir = "org/acme/lib/1.0-SNAPSHOT/";
        await PutOkAsync(dir + "lib-1.0-20240101.100000-1.jar", Bytes("build one"));
        await PutOkAsync(dir + "lib-1.0-20240101.100000-1.pom", Bytes(PomFor("lib", "1.0-SNAPSHOT")));
        await PutOkAsync(dir + "lib-1.0-20240102.110000-2.jar", Bytes("build two"));
        await PutOkAsync(dir + "lib-1.0-20240102.110000-2-sources.jar", Bytes("sources two"));

        var xml = XDocument.Parse(await GetTextAsync(dir + "maven-metadata.xml", key: null));

        var versioning = xml.Root!.Element("versioning")!;
        Assert.Equal("1.0-SNAPSHOT", xml.Root.Element("version")!.Value);
        Assert.Equal("20240102.110000", versioning.Element("snapshot")!.Element("timestamp")!.Value);
        Assert.Equal("2", versioning.Element("snapshot")!.Element("buildNumber")!.Value);
        var jar = versioning.Element("snapshotVersions")!.Elements("snapshotVersion").Single(s => s.Element("extension")!.Value == "jar" && s.Element("classifier") == null);
        Assert.Equal("1.0-20240102.110000-2", jar.Element("value")!.Value);

        // A Maven 2 client asks for the SNAPSHOT file name itself.
        Assert.Equal("build two", await GetTextAsync(dir + "lib-1.0-SNAPSHOT.jar", key: null));
        Assert.Equal("sources two", await GetTextAsync(dir + "lib-1.0-SNAPSHOT-sources.jar", key: null));
        Assert.Equal("build one", await GetTextAsync(dir + "lib-1.0-20240101.100000-1.jar", key: null));
    }

    [Fact]
    public async Task OnlyTheNewestSnapshotBuildsAreKeptWhenTheRepositoryLimitsThem()
    {
        await CreateAsync(policy: "Snapshots", snapshotBuilds: 2);
        var dir = "org/acme/lib/1.0-SNAPSHOT/";
        for (var build = 1; build <= 4; build++)
        {
            await PutOkAsync($"{dir}lib-1.0-20240101.10000{build}-{build}.jar", Bytes("build " + build));
            await PutOkAsync($"{dir}lib-1.0-20240101.10000{build}-{build}.pom", Bytes(PomFor("lib", "1.0-SNAPSHOT")));
        }

        using var oldest = await SendAsync(HttpMethod.Get, dir + "lib-1.0-20240101.100001-1.jar", key: null);
        using var second = await SendAsync(HttpMethod.Get, dir + "lib-1.0-20240101.100002-2.jar", key: null);
        using var third = await SendAsync(HttpMethod.Get, dir + "lib-1.0-20240101.100003-3.jar", key: null);

        Assert.Equal(HttpStatusCode.NotFound, oldest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal("build 4", await GetTextAsync(dir + "lib-1.0-SNAPSHOT.jar", key: null));
    }

    // ---- policy ----

    [Fact]
    public async Task AReleasesRepositoryRefusesASnapshotWith409()
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Put, "org/acme/lib/1.0-SNAPSHOT/lib-1.0-20240101.100000-1.jar", Bytes("x"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("release", await TextAsync(response), StringComparison.OrdinalIgnoreCase);
        using var metadata = await SendAsync(HttpMethod.Get, "org/acme/lib/maven-metadata.xml", key: null);
        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
    }

    [Fact]
    public async Task ASnapshotsRepositoryRefusesAReleaseWith409()
    {
        await CreateAsync(policy: "Snapshots");

        using var response = await SendAsync(HttpMethod.Put, Jar, Bytes("x"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ABothRepositoryTakesReleasesAndSnapshots()
    {
        await CreateAsync(policy: "Both");

        await PutOkAsync(Jar, Bytes("x"));
        await PutOkAsync("org/acme/lib/2.0-SNAPSHOT/lib-2.0-20240101.100000-1.jar", Bytes("y"));
    }

    // ---- redeploy ----

    [Fact]
    public async Task AReleaseFileThatExistsCannotBeDeployedAgainByDefault()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("first"));

        using var response = await SendAsync(HttpMethod.Put, Jar, Bytes("second"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("first", await GetTextAsync(Jar, key: null));
    }

    [Fact]
    public async Task ARedeployReplacesTheFileAndItsChecksumsWhenTheRepositoryAllowsIt()
    {
        await CreateAsync(redeploy: true);
        await PutOkAsync(Jar, Bytes("first"));

        await PutOkAsync(Jar, Bytes("second"));

        Assert.Equal("second", await GetTextAsync(Jar, key: null));
        Assert.Equal(Hex(SHA1.HashData(Bytes("second"))), await GetTextAsync(Jar + ".sha1", key: null));
    }

    // ---- names ----

    [Theory]
    [InlineData("org/acme/lib/1.0/other-1.0.jar")]
    [InlineData("org/acme/lib/1.0/lib-2.0.jar")]
    [InlineData("org/acme/lib/1.0/lib-1.0")]
    [InlineData("org/acme/lib/1.0/lib-1.0.1.jar")]
    public async Task AFileNameThatIsNotTheLayoutsIsRefusedWith400(string path)
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Put, path, Bytes("x"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("jar")]
    [InlineData("org/acme/lib.jar")]
    [InlineData("org/ac me/lib/1.0/lib-1.0.jar")]
    [InlineData("org/acme/lib/1.0/lib-1.0.jar%0A")]
    public async Task APathThatIsNotMavensIsRefusedWith400(string path)
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Put, path, Bytes("x"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task APathThatClimbsOutOfItsFolderNeverReachesTheStore()
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Put, "org/acme/../../../escape/1.0/escape-1.0.jar", Bytes("x"));

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, response.StatusCode.ToString());
    }

    [Fact]
    public async Task ANameThatDiffersFromAnExistingOneOnlyInLetterCaseIsRefusedWith409()
    {
        // Coordinates are case-sensitive, but a file system that is not would keep both spellings in one folder.
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("lower"));

        using var group = await SendAsync(HttpMethod.Put, "Org/acme/lib/1.0/lib-1.0.jar", Bytes("upper"));
        using var artifact = await SendAsync(HttpMethod.Put, "org/acme/Lib/1.0/Lib-1.0.jar", Bytes("upper"));
        using var version = await SendAsync(HttpMethod.Put, "org/acme/lib/1.0/lib-1.0.POM", Bytes("<project/>"));
        using var rc = await SendAsync(HttpMethod.Put, "org/acme/lib/1.0-RC1/lib-1.0-RC1.jar", Bytes("rc"));
        using var rcLower = await SendAsync(HttpMethod.Put, "org/acme/lib/1.0-rc1/lib-1.0-rc1.jar", Bytes("rc"));

        Assert.Equal(HttpStatusCode.Conflict, group.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, artifact.StatusCode);
        Assert.Equal(HttpStatusCode.Created, version.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rc.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rcLower.StatusCode);
        Assert.Contains("letter case", await TextAsync(group));
        Assert.Equal("lower", await GetTextAsync(Jar, key: null));
        using var read = await SendAsync(HttpMethod.Get, "Org/acme/lib/1.0/lib-1.0.jar", key: null);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    // ---- authentication ----

    [Fact]
    public async Task ADeployWithoutCredentialsIsChallengedWithBasic()
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Put, Jar, Bytes("x"), key: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Basic");
    }

    [Fact]
    public async Task ADeployWithAWrongKeyIsRefused()
    {
        await CreateAsync();

        using var response = await SendAsync(HttpMethod.Put, Jar, Bytes("x"), key: "not-the-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task APrivateRepositoryChallengesAnonymousReadsAndServesThoseWithTheKey()
    {
        await CreateAsync(anonymousRead: false);
        await PutOkAsync(Jar, Bytes("secret"));

        using var anonymous = await SendAsync(HttpMethod.Get, Jar, key: null);
        using var metadata = await SendAsync(HttpMethod.Get, "org/acme/lib/maven-metadata.xml", key: null);
        using var authorized = await SendAsync(HttpMethod.Get, Jar);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains(anonymous.Headers.WwwAuthenticate, h => h.Scheme == "Basic");
        Assert.Equal(HttpStatusCode.Unauthorized, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    // ---- delete ----

    [Fact]
    public async Task ADeletedFileGoesAndTheLastFileTakesTheVersionAndTheArtifactWithIt()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("jar"));
        await PutOkAsync("org/acme/lib/1.0/lib-1.0.pom", Bytes(PomFor("lib", "1.0")));

        using var first = await SendAsync(HttpMethod.Delete, Jar);
        using var gone = await SendAsync(HttpMethod.Get, Jar, key: null);
        using var stillThere = await SendAsync(HttpMethod.Get, "org/acme/lib/maven-metadata.xml", key: null);
        using var last = await SendAsync(HttpMethod.Delete, "org/acme/lib/1.0/lib-1.0.pom");
        using var metadata = await SendAsync(HttpMethod.Get, "org/acme/lib/maven-metadata.xml", key: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, last.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
    }

    [Fact]
    public async Task ADeleteNeedsCredentialsAndAMissingFileIs404()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("jar"));

        using var anonymous = await SendAsync(HttpMethod.Delete, Jar, key: null);
        using var missing = await SendAsync(HttpMethod.Delete, "org/acme/lib/1.0/lib-1.0-sources.jar");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("jar", await GetTextAsync(Jar, key: null));
    }

    [Fact]
    public async Task ADeletedReleaseCanBeDeployedAgain()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("first"));
        using (await SendAsync(HttpMethod.Delete, Jar)) { }

        await PutOkAsync(Jar, Bytes("second"));

        Assert.Equal("second", await GetTextAsync(Jar, key: null));
    }

    // ---- formats and repositories ----

    [Fact]
    public async Task TheMavenRoutesAreNotServedByTheDefaultNuGetRepositoryNorByAnNpmOne()
    {
        await _app.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);

        using var defaultRepo = await SendAsync(HttpMethod.Put, Jar, Bytes("x"), repo: "/maven/");
        using var npm = await SendAsync(HttpMethod.Put, Jar, Bytes("x"), repo: "/r/web/maven/");
        using var read = await SendAsync(HttpMethod.Get, Jar, key: null, repo: "/maven/");

        Assert.Equal(HttpStatusCode.NotFound, defaultRepo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, npm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task TheNuGetAndNpmRoutesAreNotServedByAMavenRepository()
    {
        await CreateAsync();

        using var service = await _client.GetAsync("/r/libs/v3/index.json", TestContext.Current.CancellationToken);
        using var npm = await _client.GetAsync("/r/libs/npm/-/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, service.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, npm.StatusCode);
    }

    [Fact]
    public async Task TheSameCoordinatesInTwoRepositoriesAreIndependent()
    {
        await CreateAsync();
        await _app.CreateMavenRepositoryAsync("other", cancellationToken: TestContext.Current.CancellationToken);
        await PutOkAsync(Jar, Bytes("in libs"));
        await PutOkAsync(Jar, Bytes("in other"), repo: "/r/other/maven/");

        using (await SendAsync(HttpMethod.Delete, Jar)) { }

        using var gone = await SendAsync(HttpMethod.Get, Jar, key: null);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("in other", await GetTextAsync(Jar, key: null, repo: "/r/other/maven/"));
    }

    // ---- the service the pages use ----

    [Fact]
    public async Task APomDeployedWithTheArtifactFeedsTheSearchAndTheVersionDetail()
    {
        await CreateAsync();
        await PutOkAsync(Jar, Bytes("jar"));
        await PutOkAsync("org/acme/lib/1.0/lib-1.0.pom", Bytes(PomFor("lib", "1.0", "The Lib")));
        await PutOkAsync("org/acme/other/2.0/other-2.0.jar", Bytes("o"));

        using var scope = await _app.CreateRepositoryScopeAsync("libs", TestContext.Current.CancellationToken);
        var maven = scope.ServiceProvider.GetRequiredService<IMavenRepository>();

        var byName = await maven.SearchAsync("the lib", 0, 10, null, TestContext.Current.CancellationToken);
        var byGroup = await maven.SearchAsync("ORG.ACME", 0, 10, null, TestContext.Current.CancellationToken);
        var detail = await maven.GetVersionAsync("org.acme", "lib", "1.0", TestContext.Current.CancellationToken);

        var hit = Assert.Single(byName.Hits);
        Assert.Equal("org.acme:lib", hit.CoordinateName);
        Assert.Equal("1.0", hit.Version);
        Assert.Equal(2, byGroup.Total);
        Assert.NotNull(detail);
        Assert.Equal("The Lib", detail!.Summary.Name);
        Assert.Equal("jar", detail.Summary.Packaging);
        Assert.Equal(new[] { "lib-1.0.jar", "lib-1.0.pom" }, detail.Files.Select(f => f.FileName));
        Assert.Equal(Hex(SHA1.HashData(Bytes("jar"))), detail.Files[0].Sha1);
        Assert.Equal("The Lib", detail.Pom!.Name);
        Assert.Contains("<artifactId>lib</artifactId>", detail.PomText);
    }

    [Fact]
    public async Task CleanupKeepsTheNewestBuildsAndDeleteVersionRemovesTheFiles()
    {
        await CreateAsync(policy: "Snapshots");
        var dir = "org/acme/lib/1.0-SNAPSHOT/";
        for (var build = 1; build <= 3; build++) await PutOkAsync($"{dir}lib-1.0-20240101.10000{build}-{build}.jar", Bytes("build " + build));

        using var scope = await _app.CreateRepositoryScopeAsync("libs", TestContext.Current.CancellationToken);
        var maven = scope.ServiceProvider.GetRequiredService<IMavenRepository>();
        var admin = new BaGetPrincipal(null, "root", isSuperuser: true, ApiKeyScopes.All, "*", isAdministrator: true);
        var snapshot = new MavenCoordinate("org.acme", "lib", "1.0-SNAPSHOT");

        var cleanup = await maven.CleanupSnapshotsAsync(admin, snapshot, 1, TestContext.Current.CancellationToken);
        var refused = await maven.CleanupSnapshotsAsync(admin, snapshot, 0, TestContext.Current.CancellationToken);
        var detail = await maven.GetVersionAsync("org.acme", "lib", "1.0-SNAPSHOT", TestContext.Current.CancellationToken);

        Assert.Equal(2, cleanup.Count);
        Assert.Equal(MavenStatus.Invalid, refused.Status);
        Assert.Equal("lib-1.0-20240101.100003-3.jar", Assert.Single(detail!.Files).FileName);

        var deleted = await maven.DeleteVersionAsync(admin, snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(1, deleted.Count);
        Assert.Null(await maven.GetArtifactAsync("org.acme", "lib", TestContext.Current.CancellationToken));
        using var gone = await SendAsync(HttpMethod.Get, dir + "lib-1.0-20240101.100003-3.jar", key: null);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task ADeployFromTheUploadPageStoresAGeneratedPomAndATimestampedSnapshot()
    {
        await CreateAsync(policy: "Both");
        using var scope = await _app.CreateRepositoryScopeAsync("libs", TestContext.Current.CancellationToken);
        var maven = scope.ServiceProvider.GetRequiredService<IMavenRepository>();
        var admin = new BaGetPrincipal(null, "root", isSuperuser: true, ApiKeyScopes.All, "*", isAdministrator: true);

        using var release = new System.IO.MemoryStream(Bytes("release"));
        using var snapshot = new System.IO.MemoryStream(Bytes("snapshot"));
        var one = await maven.DeployAsync(admin, new MavenDeployment { GroupId = "org.acme", ArtifactId = "lib", Version = "3.0", File = release }, TestContext.Current.CancellationToken);
        var two = await maven.DeployAsync(admin, new MavenDeployment { GroupId = "org.acme", ArtifactId = "lib", Version = "4.0-SNAPSHOT", File = snapshot }, TestContext.Current.CancellationToken);

        Assert.True(one.Succeeded, one.Message);
        Assert.True(two.Succeeded, two.Message);
        Assert.Contains("<artifactId>lib</artifactId>", await GetTextAsync("org/acme/lib/3.0/lib-3.0.pom", key: null));
        Assert.Equal("release", await GetTextAsync("org/acme/lib/3.0/lib-3.0.jar", key: null));
        Assert.Equal("snapshot", await GetTextAsync("org/acme/lib/4.0-SNAPSHOT/lib-4.0-SNAPSHOT.jar", key: null));
    }
}

/// <summary>The size limit of a Maven deploy (<c>Maven:MaxFileBytes</c>).</summary>
public class MavenSizeLimitTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public MavenSizeLimitTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "ApiKey", "k" }, { "Maven:MaxFileBytes", "16" } });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private HttpRequestMessage Request(HttpMethod method, byte[]? body = null)
    {
        var request = new HttpRequestMessage(method, "/r/libs/maven/org/acme/lib/1.0/lib-1.0.jar");
        if (body != null) request.Content = new ByteArrayContent(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("u:k")));

        return request;
    }

    [Fact]
    public async Task AFileOverTheConfiguredSizeIsRefusedWith413AndNothingIsStored()
    {
        await _app.CreateMavenRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken);

        using var tooLarge = await _client.SendAsync(Request(HttpMethod.Put, new byte[64]), TestContext.Current.CancellationToken);
        using var fits = await _client.SendAsync(Request(HttpMethod.Put, new byte[16]), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
    }

    [Fact]
    public async Task ABodyLargerThanItsDeclaredLengthIsStillStoppedAtTheLimit()
    {
        await _app.CreateMavenRepositoryAsync("libs", cancellationToken: TestContext.Current.CancellationToken);
        using var request = Request(HttpMethod.Put);
        request.Content = new StreamContent(new System.IO.MemoryStream(new byte[64]));

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
