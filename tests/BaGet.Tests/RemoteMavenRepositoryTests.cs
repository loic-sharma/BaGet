using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Remote Maven repositories (P4): a repository that caches an upstream such as Maven Central. The upstream is a
/// <see cref="FakeMavenUpstream"/> in memory, reached through <see cref="ForwardingRemoteHttpClientFactory"/> (which counts what it
/// receives and can pretend to be down); time is a <see cref="ManualTimeProvider"/>. The real <c>mvn</c> against a real upstream is in
/// <c>BaGet.Clients.Tests</c>.
/// </summary>
public class RemoteMavenRepositoryTests : IDisposable
{
    private const string Key = "secret-key";
    private const string Remote = "central";
    private const string Repo = "/r/central/maven/";
    private const string UpstreamUrl = "http://repo.test/maven2/";
    private const string Folder = "org/acme/lib";
    private const string Jar = "org/acme/lib/1.0/lib-1.0.jar";
    private const string Pom = "org/acme/lib/1.0/lib-1.0.pom";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeMavenUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly ManualTimeProvider _time = new();
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public RemoteMavenRepositoryTests(ITestOutputHelper output)
    {
        _wire = new ForwardingRemoteHttpClientFactory(new HttpClient(_upstream));
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", Key } },
            configureServices: services =>
            {
                services.AddSingleton<IRemoteHttpClientFactory>(_wire);
                services.AddSingleton(new RemoteRepositoryState(_time));
            });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    private static RemoteRepositorySettings Settings(Action<RemoteRepositorySettings>? change = null)
    {
        // The form of a Maven repository posts the NuGet default protocol; the repository's format decides.
        var settings = new RemoteRepositorySettings { Url = UpstreamUrl, Protocol = RemoteProtocol.V3, MetadataTtlSeconds = 600, NegativeTtlSeconds = 60 };
        change?.Invoke(settings);

        return settings;
    }

    private async Task CreateRemoteAsync(Action<RemoteRepositorySettings>? change = null, string name = Remote, bool anonymousRead = true)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Maven, RepositoryKind.Remote, Settings(change), anonymousRead, Cancel);
    }

    private async Task UpdateAsync(Action<RemoteRepositorySettings> change, string name = Remote)
    {
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var current = (await admin.FindAsync(name, Cancel))!.Settings;
        change(current);
        await admin.UpdateAsync(name, current, clearPassword: false, Cancel);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? key = null, byte[]? body = null, string repo = Repo)
    {
        using var request = new HttpRequestMessage(method, repo + path);
        if (body != null) request.Content = new ByteArrayContent(body);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("someone:" + key)));

        return await _client.SendAsync(request, Cancel);
    }

    private Task<HttpResponseMessage> GetAsync(string path, string repo = Repo) => SendAsync(HttpMethod.Get, path, repo: repo);

    private async Task<string> TextAsync(string path)
    {
        using var response = await GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {response.StatusCode}");

        return await response.Content.ReadAsStringAsync(Cancel);
    }

    private async Task<(int Versions, int Artifacts)> StoredAsync(string repository = Remote)
    {
        using var scope = _app.Services.CreateScope();
        var overview = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(repository, Cancel);

        return (overview.PackageCount, overview.PackageIdCount);
    }

    // ---- creation ----

    [Fact]
    public async Task ARemoteMavenRepositoryIsCreatedWithTheMavenProtocolWhateverTheFormSent()
    {
        await CreateRemoteAsync();

        using var scope = _app.Services.CreateScope();
        var info = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Remote, Cancel);
        Assert.Equal(RepositoryKind.Remote, info!.Kind);
        Assert.Equal(RepositoryFormat.Maven, info.Format);

        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Remote, Cancel);
        Assert.Equal(RemoteProtocol.Maven, detail!.Settings.Protocol);
        Assert.Equal(UpstreamUrl, detail.Settings.Url);
    }

    [Fact]
    public async Task ASavedRemoteMavenRepositoryKeepsTheMavenProtocol()
    {
        await CreateRemoteAsync();

        await UpdateAsync(s =>
        {
            s.Protocol = RemoteProtocol.V2;
            s.MetadataTtlSeconds = 5;
        });

        using var scope = _app.Services.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().FindAsync(Remote, Cancel);
        Assert.Equal(RemoteProtocol.Maven, detail!.Settings.Protocol);
        Assert.Equal(5, detail.Settings.MetadataTtlSeconds);
    }

    [Fact]
    public async Task AMavenUpstreamUrlWithAQueryStringIsRefused()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => CreateRemoteAsync(s => s.Url = UpstreamUrl + "?x=1"));

        Assert.Contains("must not have a query string", error.Message);
    }

    // ---- files ----

    [Fact]
    public async Task AFileIsFetchedFromTheUpstreamServedByteForByteAndKeptForTheNextRequest()
    {
        var (_, jar) = _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();

        using (var first = await GetAsync(Jar))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(jar, await first.Content.ReadAsByteArrayAsync(Cancel));
            Assert.Equal("application/java-archive", first.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Hex(SHA1.HashData(jar)), first.Headers.GetValues("X-Checksum-Sha1").Single());
        }

        using (var second = await GetAsync(Jar))
        {
            Assert.Equal(jar, await second.Content.ReadAsByteArrayAsync(Cancel));
        }

        Assert.Equal(1, _upstream.Gets("lib-1.0.jar"));
        Assert.Equal((1, 1), await StoredAsync());
    }

    [Fact]
    public async Task AKeptFileHasItsRowsAndIsFoundBySearchAndThePages()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();
        (await GetAsync(Pom)).Dispose();
        (await GetAsync(Jar)).Dispose();

        using var scope = await _app.CreateRepositoryScopeAsync(Remote, Cancel);
        var maven = scope.ServiceProvider.GetRequiredService<IMavenRepository>();
        var artifact = await maven.GetArtifactAsync("org.acme", "lib", Cancel);
        Assert.Equal("1.0", artifact!.Versions.Single().Version);
        var detail = await maven.GetVersionAsync("org.acme", "lib", "1.0", Cancel);
        Assert.Equal(new[] { "lib-1.0.jar", "lib-1.0.pom" }, detail!.Files.Select(f => f.FileName));
        Assert.Equal("From upstream lib", detail.Pom!.Name);
        Assert.Single((await maven.SearchAsync("upstream", 0, 10, null, Cancel)).Hits);

        using var browse = await _client.GetAsync("/r/central/maven-artifacts", Cancel);
        var html = await browse.Content.ReadAsStringAsync(Cancel);
        Assert.Equal(HttpStatusCode.OK, browse.StatusCode);
        Assert.Contains("org.acme:lib", html);
        Assert.Contains("caches an upstream repository", html);
        Assert.DoesNotContain("Upload an artifact", html);
    }

    [Fact]
    public async Task TheChecksumOfAFileNotKeptYetIsTheUpstreamsAndTheFileIsNotDownloadedForIt()
    {
        var (_, jar) = _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();

        Assert.Equal(Hex(SHA1.HashData(jar)), await TextAsync(Jar + ".sha1"));
        Assert.Equal(Hex(SHA256.HashData(jar)), await TextAsync(Jar + ".sha256"));

        Assert.Equal(0, _upstream.Gets("lib-1.0.jar"));
        Assert.Equal((0, 0), await StoredAsync());
    }

    [Theory]
    [InlineData("sha1")]
    [InlineData("md5")]
    [InlineData("sha256")]
    [InlineData("sha512")]
    public async Task TheChecksumsOfAKeptFileAreComputedFromIt(string algorithm)
    {
        var (_, jar) = _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();
        (await GetAsync(Jar)).Dispose();
        var before = _upstream.Requests.Length;

        var expected = algorithm switch
        {
            "sha1" => Hex(SHA1.HashData(jar)),
            "md5" => Hex(MD5.HashData(jar)),
            "sha256" => Hex(SHA256.HashData(jar)),
            _ => Hex(SHA512.HashData(jar)),
        };

        Assert.Equal(expected, await TextAsync($"{Jar}.{algorithm}"));
        Assert.Equal(before, _upstream.Requests.Length);
    }

    [Fact]
    public async Task AHeadRequestForAFileNotKeptYetAsksTheUpstreamForTheHeadersOnlyAndKeepsNothing()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0", jarText: "0123456789");
        await CreateRemoteAsync();

        using var response = await SendAsync(HttpMethod.Head, Jar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, response.Content.Headers.ContentLength);
        Assert.Contains(_upstream.Requests, r => r.Method == "HEAD" && r.Path == Jar);
        Assert.Equal(0, _upstream.Gets("lib-1.0.jar"));
        Assert.Equal((0, 0), await StoredAsync());
    }

    [Fact]
    public async Task AFileTheUpstreamDoesNotHaveIs404AndThatIsRememberedForTheNegativePeriod()
    {
        await CreateRemoteAsync();

        using (var first = await GetAsync(Jar)) Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        using (var second = await GetAsync(Jar)) Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(1, _upstream.Count(Jar));

        _time.Advance(TimeSpan.FromSeconds(61));
        _upstream.AddRelease("org.acme", "lib", "1.0");
        using var third = await GetAsync(Jar);

        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Fact]
    public async Task ADownloadTheUpstreamsChecksumContradictsIsNeitherServedNorKept()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.Corrupt.Add(Jar);
        await CreateRemoteAsync();

        using var response = await GetAsync(Jar);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("does not match", await response.Content.ReadAsStringAsync(Cancel));
        Assert.Equal((0, 0), await StoredAsync());
    }

    [Fact]
    public async Task AFileWhoseChecksumsTheUpstreamDoesNotPublishIsKeptUnverified()
    {
        var (_, jar) = _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.NoChecksums = true;
        await CreateRemoteAsync();

        using var response = await GetAsync(Jar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(jar, await response.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal((1, 1), await StoredAsync());
    }

    [Fact]
    public async Task AnHtmlPageAnUpstreamAnswers200ForAMissingFileIsNotTakenForTheFile()
    {
        _upstream.HtmlForMissing = true;
        await CreateRemoteAsync();

        using var file = await GetAsync(Jar);
        using var metadata = await GetAsync(Folder + "/maven-metadata.xml");

        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, metadata.StatusCode);
        Assert.Equal((0, 0), await StoredAsync());
    }

    [Fact]
    public async Task ConcurrentRequestsForOneFileDownloadItOnce()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var response = await GetAsync(Jar);

            return (response.StatusCode, Bytes: await response.Content.ReadAsByteArrayAsync(Cancel));
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(responses.Select(r => Convert.ToHexString(r.Bytes)).Distinct());
        Assert.Equal(1, _upstream.Gets("lib-1.0.jar"));
        Assert.Equal((1, 1), await StoredAsync());
    }

    [Fact]
    public async Task WithoutStoringLocallyTheRepositoryIsAPassThroughAndKeepsNothing()
    {
        var (_, jar) = _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync(s => s.StoreLocally = false);

        for (var i = 0; i < 2; i++)
        {
            using var response = await GetAsync(Jar);
            Assert.Equal(jar, await response.Content.ReadAsByteArrayAsync(Cancel));
        }

        Assert.Equal(2, _upstream.Gets("lib-1.0.jar"));
        Assert.Equal((0, 0), await StoredAsync());
    }

    // ---- snapshots ----

    [Fact]
    public async Task ATimestampedSnapshotBuildIsKeptButTheMovingNameIsAlwaysAskedOfTheUpstream()
    {
        const string stem = "org/acme/lib/1.0-SNAPSHOT/lib-1.0-";
        _upstream.AddFile("org.acme", "lib", "1.0-SNAPSHOT", "lib-1.0-20240101.120000-1.jar", Encoding.UTF8.GetBytes("build one"));
        await CreateRemoteAsync();

        using (var timestamped = await GetAsync(stem + "20240101.120000-1.jar"))
        {
            Assert.Equal("build one", await timestamped.Content.ReadAsStringAsync(Cancel));
        }

        (await GetAsync(stem + "20240101.120000-1.jar")).Dispose();
        Assert.Equal(1, _upstream.Gets("20240101.120000-1.jar"));
        Assert.Equal((1, 1), await StoredAsync());

        // The moving name follows the upstream, not the build that was kept.
        _upstream.AddFile("org.acme", "lib", "1.0-SNAPSHOT", "lib-1.0-SNAPSHOT.jar", Encoding.UTF8.GetBytes("moving one"));
        using (var moving = await GetAsync("org/acme/lib/1.0-SNAPSHOT/lib-1.0-SNAPSHOT.jar"))
        {
            Assert.Equal("moving one", await moving.Content.ReadAsStringAsync(Cancel));
        }

        _upstream.AddFile("org.acme", "lib", "1.0-SNAPSHOT", "lib-1.0-SNAPSHOT.jar", Encoding.UTF8.GetBytes("moving two"));
        using (var again = await GetAsync("org/acme/lib/1.0-SNAPSHOT/lib-1.0-SNAPSHOT.jar"))
        {
            Assert.Equal("moving two", await again.Content.ReadAsStringAsync(Cancel));
        }

        Assert.Equal((1, 1), await StoredAsync());
    }

    [Fact]
    public async Task TheMovingSnapshotNameFallsBackToTheNewestKeptBuildWhileTheUpstreamIsDown()
    {
        _upstream.AddFile("org.acme", "lib", "1.0-SNAPSHOT", "lib-1.0-20240101.120000-1.jar", Encoding.UTF8.GetBytes("build one"));
        await CreateRemoteAsync();
        (await GetAsync("org/acme/lib/1.0-SNAPSHOT/lib-1.0-20240101.120000-1.jar")).Dispose();

        _wire.Down = true;
        using var response = await GetAsync("org/acme/lib/1.0-SNAPSHOT/lib-1.0-SNAPSHOT.jar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("build one", await response.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task TheVersionLevelMetadataOfASnapshotIsTheUpstreams()
    {
        const string xml = "<metadata modelVersion=\"1.1.0\"><groupId>org.acme</groupId><artifactId>lib</artifactId><version>1.0-SNAPSHOT</version><versioning><snapshot><timestamp>20240101.120000</timestamp><buildNumber>1</buildNumber></snapshot></versioning></metadata>";
        _upstream.SetMetadata(Folder + "/1.0-SNAPSHOT/maven-metadata.xml", xml);
        await CreateRemoteAsync();

        var served = XDocument.Parse(await TextAsync(Folder + "/1.0-SNAPSHOT/maven-metadata.xml"));

        Assert.Equal("20240101.120000", served.Root!.Element("versioning")!.Element("snapshot")!.Element("timestamp")!.Value);
    }

    // ---- metadata ----

    [Fact]
    public async Task TheArtifactMetadataIsTheUpstreamsAndItsChecksumsDescribeWhatWasServed()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.AddRelease("org.acme", "lib", "1.1");
        await CreateRemoteAsync();

        var xml = await TextAsync(Folder + "/maven-metadata.xml");
        var served = XDocument.Parse(xml);

        Assert.Equal(new[] { "1.0", "1.1" }, served.Root!.Element("versioning")!.Element("versions")!.Elements("version").Select(v => v.Value));
        Assert.Equal("1.1", served.Root.Element("versioning")!.Element("release")!.Value);
        Assert.Equal(Hex(SHA1.HashData(Encoding.UTF8.GetBytes(xml))), await TextAsync(Folder + "/maven-metadata.xml.sha1"));
    }

    [Fact]
    public async Task AMetadataAnswerInsideThePeriodIsNotAskedForAgainAndAnExpiredOneIs()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();

        await TextAsync(Folder + "/maven-metadata.xml");
        await TextAsync(Folder + "/maven-metadata.xml");
        Assert.Equal(1, _upstream.Gets("maven-metadata.xml"));

        _time.Advance(TimeSpan.FromSeconds(601));
        _upstream.AddRelease("org.acme", "lib", "1.2");
        var xml = await TextAsync(Folder + "/maven-metadata.xml");

        Assert.Equal(2, _upstream.Gets("maven-metadata.xml"));
        Assert.Contains("<version>1.2</version>", xml);
    }

    [Fact]
    public async Task AnExpiredMetadataAnswerIsServedWhileTheUpstreamIsDown()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();
        await TextAsync(Folder + "/maven-metadata.xml");

        _time.Advance(TimeSpan.FromSeconds(601));
        _wire.Down = true;
        var xml = await TextAsync(Folder + "/maven-metadata.xml");

        Assert.Contains("<version>1.0</version>", xml);
    }

    [Fact]
    public async Task AGroupsPluginListIsNotJudgedByAnUnrelatedArtifactsName()
    {
        // org/acme/maven-metadata.xml can be artifact org:acme or the plugin list of group org.acme: the document decides.
        _upstream.SetMetadata("org/acme/maven-metadata.xml", "<metadata><plugins><plugin><name>P</name><prefix>p</prefix><artifactId>acme-maven-plugin</artifactId></plugin></plugins></metadata>");
        await CreateRemoteAsync();

        using var scope = await _app.CreateRepositoryScopeAsync(Remote, Cancel);
        var path = MavenNames.ParsePath("org/acme/maven-metadata.xml")!;

        Assert.Null(await scope.ServiceProvider.GetRequiredService<IMavenRepository>().ResolveAsync(path, Cancel));
    }

    [Fact]
    public async Task TheGroupLevelMetadataThatResolvesPluginPrefixesIsTheUpstreams()
    {
        const string xml = "<metadata><plugins><plugin><name>Maven Dependency Plugin</name><prefix>dependency</prefix><artifactId>maven-dependency-plugin</artifactId></plugin></plugins></metadata>";
        _upstream.SetMetadata("org/apache/maven/plugins/maven-metadata.xml", xml);
        await CreateRemoteAsync();

        var served = await TextAsync("org/apache/maven/plugins/maven-metadata.xml");

        Assert.Contains("<prefix>dependency</prefix>", served);
    }

    [Fact]
    public async Task MetadataIsGeneratedFromWhatWasKeptWhenTheUpstreamIsDown()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();
        (await GetAsync(Pom)).Dispose();

        _time.Advance(TimeSpan.FromHours(2));
        _wire.Down = true;
        var xml = await TextAsync(Folder + "/maven-metadata.xml");

        Assert.Contains("<version>1.0</version>", xml);
    }

    [Fact]
    public async Task MetadataTheUpstreamHasNeitherNorTheRepositoryKeptIs404()
    {
        await CreateRemoteAsync();

        using var response = await GetAsync(Folder + "/maven-metadata.xml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- an upstream that is down, offline or refuses ----

    [Fact]
    public async Task AFileKeptEarlierIsServedWhileTheUpstreamIsDownAndAnUncachedOneIs502()
    {
        var (_, jar) = _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.AddRelease("org.acme", "other", "1.0");
        await CreateRemoteAsync();
        (await GetAsync(Jar)).Dispose();

        _wire.Down = true;
        using var kept = await GetAsync(Jar);
        using var missing = await GetAsync("org/acme/other/1.0/other-1.0.jar");

        Assert.Equal(jar, await kept.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Equal(HttpStatusCode.BadGateway, missing.StatusCode);
    }

    [Fact]
    public async Task OfflineModeServesWhatIsKeptAndNeverContactsTheUpstream()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.AddRelease("org.acme", "other", "1.0");
        await CreateRemoteAsync();
        (await GetAsync(Jar)).Dispose();
        await UpdateAsync(s => s.Offline = true);
        var before = _upstream.Requests.Length;

        using var kept = await GetAsync(Jar);
        using var missing = await GetAsync("org/acme/other/1.0/other-1.0.jar");
        using var metadata = await GetAsync("org/acme/other/maven-metadata.xml");

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
        Assert.Equal(before, _upstream.Requests.Length);
    }

    [Fact]
    public async Task TheCredentialsAreSentToTheUpstreamAsBasicAndWrongOnesAre502()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.RequiredAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("robot:pass"));
        await CreateRemoteAsync(s =>
        {
            s.Username = "robot";
            s.Password = "pass";
        });

        using var ok = await GetAsync(Jar);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.All(_upstream.Requests, r => Assert.Equal(_upstream.RequiredAuthorization, r.Authorization));

        await UpdateAsync(s => s.Password = "wrong");
        _upstream.AddRelease("org.acme", "other", "1.0");
        using var refused = await GetAsync("org/acme/other/1.0/other-1.0.jar");

        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        var text = await refused.Content.ReadAsStringAsync(Cancel);
        Assert.DoesNotContain("wrong", text);
        Assert.DoesNotContain("repo.test", text);
    }

    // ---- writes, access ----

    [Fact]
    public async Task AWriteIsRefusedWhoeverSendsItAndNothingReachesTheUpstream()
    {
        await CreateRemoteAsync();

        using var put = await SendAsync(HttpMethod.Put, Jar, Key, Encoding.UTF8.GetBytes("x"));
        using var anonymous = await SendAsync(HttpMethod.Put, Jar, null, Encoding.UTF8.GetBytes("x"));
        using var delete = await SendAsync(HttpMethod.Delete, Jar, Key);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Empty(_upstream.Requests);
    }

    [Fact]
    public async Task APrivateRepositoryAsksNothingOfItsUpstreamForACallerWhoMayNotReadIt()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync(anonymousRead: false);

        // The metadata path that could name a version or an artifact makes the repository look at its upstream; nobody who
        // cannot read the repository may cause that.
        using var response = await GetAsync("org/acme/lib/1.0/maven-metadata.xml");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_upstream.Requests);
    }

    [Fact]
    public async Task APathOutsideTheLayoutIsNeverSentToTheUpstream()
    {
        await CreateRemoteAsync();

        using var traversal = await GetAsync("org/../secret/1.0/x.jar");
        using var shallow = await GetAsync("lib-1.0.jar");

        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, shallow.StatusCode);
        Assert.Empty(_upstream.Requests);
    }

    // ---- the administration ----

    [Fact]
    public async Task TheCacheOverviewCountsTheKeptVersionsAndArtifactsAndPurgingEmptiesThem()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.AddRelease("org.acme", "lib", "1.1");
        _upstream.AddRelease("org.acme", "other", "1.0");
        await CreateRemoteAsync();
        foreach (var path in new[] { Jar, "org/acme/lib/1.1/lib-1.1.jar", "org/acme/other/1.0/other-1.0.jar" }) (await GetAsync(path)).Dispose();
        await TextAsync(Folder + "/maven-metadata.xml");
        Assert.Equal((3, 2), await StoredAsync());

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var result = await admin.PurgeAsync(Remote, Cancel);

        Assert.Equal(3, result.Packages);
        Assert.True(result.CachedAnswers > 0);
        Assert.Equal((0, 0), await StoredAsync());
        Assert.Equal(0, (await admin.GetCacheAsync(Remote, Cancel)).Status.MetadataEntries);
    }

    [Fact]
    public async Task PurgingOneVersionOfAnArtifactDeletesItsFilesAndFetchesThemAgain()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.AddRelease("org.acme", "lib", "1.1");
        await CreateRemoteAsync();
        (await GetAsync(Jar)).Dispose();
        (await GetAsync("org/acme/lib/1.1/lib-1.1.jar")).Dispose();
        await TextAsync(Folder + "/maven-metadata.xml");

        using (var scope = _app.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().PurgeArtifactAsync(Remote, "org.acme:lib", "1.0", Cancel);
            Assert.Equal(1, result.Packages);
        }

        Assert.Equal((1, 1), await StoredAsync());

        // 1.1 stays kept, 1.0 and the version list are asked again.
        (await GetAsync(Jar)).Dispose();
        (await GetAsync("org/acme/lib/1.1/lib-1.1.jar")).Dispose();
        await TextAsync(Folder + "/maven-metadata.xml");
        Assert.Equal(2, _upstream.Gets("lib-1.0.jar"));
        Assert.Equal(1, _upstream.Gets("lib-1.1.jar"));
        Assert.Equal(2, _upstream.Gets("maven-metadata.xml"));
    }

    [Fact]
    public async Task PurgingAnArtifactDeletesEveryVersionAndRefusesWhatIsNotAnArtifact()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        _upstream.AddRelease("org.acme", "lib", "1.1");
        await CreateRemoteAsync();
        (await GetAsync(Jar)).Dispose();
        (await GetAsync("org/acme/lib/1.1/lib-1.1.jar")).Dispose();

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        await Assert.ThrowsAsync<ArgumentException>(() => admin.PurgeArtifactAsync(Remote, "lib", null, Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => admin.PurgeArtifactAsync(Remote, "org.acme:lib", "../x", Cancel));

        var result = await admin.PurgeArtifactAsync(Remote, "org.acme:lib", null, Cancel);

        Assert.Equal(2, result.Packages);
        Assert.Equal((0, 0), await StoredAsync());
    }

    [Fact]
    public async Task TestConnectionAnswersForAListingRootAForbiddingOneAndOneThatListsNothing()
    {
        await CreateRemoteAsync();
        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var input = Settings();

        var listed = await admin.TestConnectionAsync(Remote, input, Cancel);
        Assert.True(listed.Success, listed.Message);
        Assert.Contains("Maven repository answered", listed.Message);

        _upstream.RootStatus = HttpStatusCode.NotFound;
        var silent = await admin.TestConnectionAsync(Remote, input, Cancel);
        Assert.True(silent.Success);
        Assert.Contains("404 for the repository root", silent.Message);

        _upstream.RootStatus = HttpStatusCode.OK;
        _upstream.RequiredAuthorization = "Basic nope";
        var refused = await admin.TestConnectionAsync(Remote, input, Cancel);
        Assert.False(refused.Success);
        Assert.Contains("refused the credentials", refused.Message);
    }

    [Fact]
    public async Task TheResolutionPreviewOfARemoteMemberSaysStoredWouldFetchOrNotFound()
    {
        _upstream.AddRelease("org.acme", "lib", "1.0");
        await CreateRemoteAsync();
        (await GetAsync(Jar)).Dispose();
        _upstream.AddRelease("org.acme", "lib", "1.1");
        _time.Advance(TimeSpan.FromSeconds(601));

        using var scope = await _app.CreateRepositoryScopeAsync(Remote, Cancel);
        var maven = scope.ServiceProvider.GetRequiredService<IMavenRepository>();

        Assert.Equal(ResolutionOutcome.Stored, await maven.ProbeAsync("org.acme", "lib", "1.0", Cancel));
        Assert.Equal(ResolutionOutcome.WouldFetch, await maven.ProbeAsync("org.acme", "lib", "1.1", Cancel));
        Assert.Equal(ResolutionOutcome.NotFound, await maven.ProbeAsync("org.acme", "lib", "9.9", Cancel));
        Assert.Equal(ResolutionOutcome.NotFound, await maven.ProbeAsync("org.acme", "nothing", "1.0", Cancel));

        await UpdateAsync(s => s.Offline = true);
        using var offline = await _app.CreateRepositoryScopeAsync(Remote, Cancel);
        Assert.Equal(ResolutionOutcome.Offline, await offline.ServiceProvider.GetRequiredService<IMavenRepository>().ProbeAsync("org.acme", "lib", "1.1", Cancel));
    }

    // ---- which artifact a metadata path names ----

    [Fact]
    public void TheMetadataDocumentSaysWhetherItsPathNamesAVersionOrAnArtifact()
    {
        // org/acme/lib/maven-metadata.xml is version "lib" of org:acme, or artifact lib of org.acme: the document decides.
        var path = MavenNames.ParsePath("org/acme/lib/maven-metadata.xml")!;
        Assert.Equal(2, path.Candidates.Count);

        var artifactLevel = Encoding.UTF8.GetBytes("<metadata><groupId>org.acme</groupId><artifactId>lib</artifactId><versioning><versions><version>1</version></versions></versioning></metadata>");
        var versionLevel = Encoding.UTF8.GetBytes("<metadata><groupId>org</groupId><artifactId>acme</artifactId><version>lib</version></metadata>");

        Assert.Equal(new MavenCoordinate("org.acme", "lib", null), RemoteMavenRepository.Identify(artifactLevel, path.Candidates));
        Assert.Equal(new MavenCoordinate("org", "acme", "lib"), RemoteMavenRepository.Identify(versionLevel, path.Candidates));
        Assert.Null(RemoteMavenRepository.Identify(Encoding.UTF8.GetBytes("<metadata><groupId>x</groupId><artifactId>y</artifactId></metadata>"), path.Candidates));
        Assert.Null(RemoteMavenRepository.Identify(Encoding.UTF8.GetBytes("not xml"), path.Candidates));
    }
}
