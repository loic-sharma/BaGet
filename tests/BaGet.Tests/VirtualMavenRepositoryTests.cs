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
/// Virtual Maven repositories (P4): <c>loc</c> (local), <c>up</c> (a remote repository over a <see cref="FakeMavenUpstream"/>) and
/// <c>v</c> = [loc, up] deploying to <c>loc</c>. Files come from the first member that has them, artifact metadata is merged with
/// <c>latest</c> and <c>release</c> following the member order, writes go to the deploy repository, a member the caller cannot read is
/// invisible, a failing member is skipped.
/// </summary>
public class VirtualMavenRepositoryTests : IDisposable
{
    private const string Key = "secret-key";
    private const string Folder = "org/acme/lib";
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly FakeMavenUpstream _upstream = new();
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public VirtualMavenRepositoryTests(ITestOutputHelper output)
    {
        _wire = new ForwardingRemoteHttpClientFactory(new HttpClient(_upstream));
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", Key } },
            configureServices: services => services.AddSingleton<IRemoteHttpClientFactory>(_wire));
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        _app.CreateMavenRepositoryAsync("loc", policy: "Both", cancellationToken: Cancel).GetAwaiter().GetResult();
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
            RepositoryFormat.Maven,
            RepositoryKind.Remote,
            new RemoteRepositorySettings { Url = "http://repo.test/maven2/", MetadataTtlSeconds = 600 },
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
            RepositoryFormat.Maven);
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string repository, string path, byte[]? body = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, $"/r/{repository}/maven/{path}");
        if (body != null) request.Content = new ByteArrayContent(body);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Bytes("deployer:" + key)));

        return await _client.SendAsync(request, Cancel);
    }

    private async Task PutAsync(string repository, string path, byte[] body)
    {
        using var response = await SendAsync(HttpMethod.Put, repository, path, body, Key);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"PUT {repository}/{path}: {response.StatusCode} {await response.Content.ReadAsStringAsync(Cancel)}");
    }

    private async Task DeployReleaseAsync(string repository, string version, string? jarText = null)
    {
        await PutAsync(repository, $"{Folder}/{version}/lib-{version}.pom", Bytes(MavenPom.Generate("org.acme", "lib", version, "jar", "Local lib", "Local description")));
        await PutAsync(repository, $"{Folder}/{version}/lib-{version}.jar", Bytes(jarText ?? $"local jar {version}"));
    }

    private async Task<string> TextAsync(string repository, string path)
    {
        using var response = await SendAsync(HttpMethod.Get, repository, path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {repository}/{path}: {response.StatusCode}");

        return await response.Content.ReadAsStringAsync(Cancel);
    }

    private async Task<HttpStatusCode> StatusAsync(string repository, string path, HttpMethod? method = null)
    {
        using var response = await SendAsync(method ?? HttpMethod.Get, repository, path);

        return response.StatusCode;
    }

    private static XElement Versioning(string xml) => XDocument.Parse(xml).Root!.Element("versioning")!;

    private async Task<(int Versions, int Artifacts)> StoredAsync(string repository)
    {
        using var scope = _app.Services.CreateScope();
        var found = await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync(repository, Cancel);

        using var inner = await _app.CreateRepositoryScopeAsync(repository, Cancel);
        var search = await inner.ServiceProvider.GetRequiredService<MavenRepositoryService>().SearchAsync(null, 0, 100, null, Cancel);

        return (found!.PackageCount, search.Total);
    }

    // ---- files ----

    [Fact]
    public async Task AFileComesFromTheFirstMemberThatHasItAndItsChecksumsFromThatSameMember()
    {
        await DeployReleaseAsync("loc", "1.0", "local one");
        _upstream.AddRelease("org.acme", "lib", "1.0", "upstream one");
        _upstream.AddRelease("org.acme", "lib", "2.0", "upstream two");

        Assert.Equal("local one", await TextAsync("v", $"{Folder}/1.0/lib-1.0.jar"));
        Assert.Equal(Hex(SHA1.HashData(Bytes("local one"))), await TextAsync("v", $"{Folder}/1.0/lib-1.0.jar.sha1"));
        Assert.Equal("upstream two", await TextAsync("v", $"{Folder}/2.0/lib-2.0.jar"));
        Assert.Equal(Hex(SHA1.HashData(Bytes("upstream two"))), await TextAsync("v", $"{Folder}/2.0/lib-2.0.jar.sha1"));

        // The upstream was never asked for what the first member holds, and what it did serve was kept in its own repository.
        Assert.Equal(0, _upstream.Count("lib-1.0.jar"));
        Assert.Equal((1, 1), await StoredAsync("up"));
        Assert.Equal((1, 1), await StoredAsync("loc"));
    }

    [Fact]
    public async Task AFileNoMemberHasIs404()
    {
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("v", $"{Folder}/9.9/lib-9.9.jar"));
    }

    [Fact]
    public async Task AHeadRequestIsAnsweredByTheFirstMemberThatHasTheFile()
    {
        _upstream.AddRelease("org.acme", "lib", "2.0", "0123456789");

        using var response = await SendAsync(HttpMethod.Head, "v", $"{Folder}/2.0/lib-2.0.jar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, response.Content.Headers.ContentLength);
        Assert.Equal((0, 0), await StoredAsync("up"));
    }

    // ---- metadata ----

    [Fact]
    public async Task TheArtifactMetadataIsTheUnionOfTheMembersVersions()
    {
        await DeployReleaseAsync("loc", "1.0");
        await DeployReleaseAsync("loc", "1.5");
        _upstream.AddRelease("org.acme", "lib", "1.5");
        _upstream.AddRelease("org.acme", "lib", "2.0");

        var xml = await TextAsync("v", $"{Folder}/maven-metadata.xml");
        var document = XDocument.Parse(xml);

        Assert.Equal("org.acme", document.Root!.Element("groupId")!.Value);
        Assert.Equal("lib", document.Root.Element("artifactId")!.Value);
        Assert.Equal(new[] { "1.0", "1.5", "2.0" }, Versioning(xml).Element("versions")!.Elements("version").Select(v => v.Value));
    }

    [Fact]
    public async Task LatestAndReleaseFollowTheMemberOrderNotTheHighestVersion()
    {
        // loc names 1.5 (its highest) and the upstream 2.0: loc is first, so 1.5 is what the virtual repository calls latest.
        await DeployReleaseAsync("loc", "1.0");
        await DeployReleaseAsync("loc", "1.5");
        _upstream.AddRelease("org.acme", "lib", "2.0");

        var versioning = Versioning(await TextAsync("v", $"{Folder}/maven-metadata.xml"));

        Assert.Equal("1.5", versioning.Element("latest")!.Value);
        Assert.Equal("1.5", versioning.Element("release")!.Value);
    }

    [Fact]
    public async Task AMemberThatComesFirstFollowsItsPlaceWhenTheOrderChanges()
    {
        await DeployReleaseAsync("loc", "1.5");
        _upstream.AddRelease("org.acme", "lib", "2.0");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
                .UpdateAsync("v", new VirtualRepositorySettings { Members = new List<string> { "up", "loc" }, DeployRepository = "loc" }, Cancel);
        }

        var versioning = Versioning(await TextAsync("v", $"{Folder}/maven-metadata.xml"));

        Assert.Equal("2.0", versioning.Element("latest")!.Value);
        Assert.Equal("2.0", versioning.Element("release")!.Value);
    }

    [Fact]
    public async Task WhenNoMemberNamesLatestOrReleaseTheHighestIsUsed()
    {
        _upstream.SetMetadata($"{Folder}/maven-metadata.xml", "<metadata><groupId>org.acme</groupId><artifactId>lib</artifactId><versioning><versions><version>1.0</version><version>3.0-SNAPSHOT</version></versions></versioning></metadata>");

        var versioning = Versioning(await TextAsync("v", $"{Folder}/maven-metadata.xml"));

        Assert.Equal("3.0-SNAPSHOT", versioning.Element("latest")!.Value);
        Assert.Equal("1.0", versioning.Element("release")!.Value);
    }

    [Fact]
    public async Task TheMetadataChecksumsDescribeTheMergedDocument()
    {
        await DeployReleaseAsync("loc", "1.0");
        _upstream.AddRelease("org.acme", "lib", "2.0");

        var xml = await TextAsync("v", $"{Folder}/maven-metadata.xml");

        Assert.Equal(Hex(SHA1.HashData(Bytes(xml))), await TextAsync("v", $"{Folder}/maven-metadata.xml.sha1"));
        Assert.Equal(Hex(MD5.HashData(Bytes(xml))), await TextAsync("v", $"{Folder}/maven-metadata.xml.md5"));
        Assert.Equal(Hex(SHA256.HashData(Bytes(xml))), await TextAsync("v", $"{Folder}/maven-metadata.xml.sha256"));
    }

    [Fact]
    public async Task TheVersionLevelMetadataOfASnapshotIsTheFirstMembersThatHasIt()
    {
        await PutAsync("loc", $"{Folder}/1.0-SNAPSHOT/lib-1.0-20240101.120000-1.jar", Bytes("local build"));
        _upstream.SetMetadata($"{Folder}/1.0-SNAPSHOT/maven-metadata.xml", "<metadata modelVersion=\"1.1.0\"><groupId>org.acme</groupId><artifactId>lib</artifactId><version>1.0-SNAPSHOT</version><versioning><snapshot><timestamp>29990101.000000</timestamp><buildNumber>9</buildNumber></snapshot></versioning></metadata>");

        var document = XDocument.Parse(await TextAsync("v", $"{Folder}/1.0-SNAPSHOT/maven-metadata.xml"));

        // loc is first and has the snapshot: the builds its document names are its own files.
        Assert.Equal("20240101.120000", document.Root!.Element("versioning")!.Element("snapshot")!.Element("timestamp")!.Value);
    }

    [Fact]
    public async Task TheVersionLevelMetadataOfASnapshotComesFromALaterMemberWhenTheFirstHasNone()
    {
        _upstream.SetMetadata($"{Folder}/1.0-SNAPSHOT/maven-metadata.xml", "<metadata modelVersion=\"1.1.0\"><groupId>org.acme</groupId><artifactId>lib</artifactId><version>1.0-SNAPSHOT</version><versioning><snapshot><timestamp>29990101.000000</timestamp><buildNumber>9</buildNumber></snapshot></versioning></metadata>");

        var document = XDocument.Parse(await TextAsync("v", $"{Folder}/1.0-SNAPSHOT/maven-metadata.xml"));

        Assert.Equal("29990101.000000", document.Root!.Element("versioning")!.Element("snapshot")!.Element("timestamp")!.Value);
    }

    [Fact]
    public async Task ThePluginPrefixesOfTheMembersAreMergedOnceEach()
    {
        await PutAsync("loc", "org/apache/maven/plugins/maven-metadata.xml", Bytes("ignored: metadata is generated"));
        _upstream.SetMetadata("org/apache/maven/plugins/maven-metadata.xml", "<metadata><plugins><plugin><name>Dependency</name><prefix>dependency</prefix><artifactId>maven-dependency-plugin</artifactId></plugin><plugin><name>Clean</name><prefix>clean</prefix><artifactId>maven-clean-plugin</artifactId></plugin></plugins></metadata>");

        var document = XDocument.Parse(await TextAsync("v", "org/apache/maven/plugins/maven-metadata.xml"));

        Assert.Equal(new[] { "dependency", "clean" }, document.Root!.Element("plugins")!.Elements("plugin").Select(p => p.Element("prefix")!.Value));
    }

    [Fact]
    public async Task MetadataNoMemberHasIs404()
    {
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("v", "org/acme/nothing/maven-metadata.xml"));
    }

    // ---- members that fail or may not be read ----

    [Fact]
    public async Task AMemberThatCannotAnswerIsSkippedAndTheOthersStillDo()
    {
        await DeployReleaseAsync("loc", "1.0");
        _upstream.AddRelease("org.acme", "lib", "2.0");
        _wire.Down = true;

        var xml = await TextAsync("v", $"{Folder}/maven-metadata.xml");
        var jar = await TextAsync("v", $"{Folder}/1.0/lib-1.0.jar");
        var status = await StatusAsync("v", $"{Folder}/2.0/lib-2.0.jar");

        Assert.Equal(new[] { "1.0" }, Versioning(xml).Element("versions")!.Elements("version").Select(v => v.Value));
        Assert.Equal("local jar 1.0", jar);
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task AMemberTheCallerMayNotReadIsInvisible()
    {
        await _app.CreateMavenRepositoryAsync("secret", policy: "Both", anonymousRead: false, cancellationToken: Cancel);
        await DeployReleaseAsync("secret", "7.0", "secret jar");
        await CreateVirtualAsync("public", new[] { "secret", "loc" }, "loc");
        await DeployReleaseAsync("loc", "1.0");

        var xml = await TextAsync("public", $"{Folder}/maven-metadata.xml");

        Assert.Equal(new[] { "1.0" }, Versioning(xml).Element("versions")!.Elements("version").Select(v => v.Value));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("public", $"{Folder}/7.0/lib-7.0.jar"));
    }

    // ---- writes ----

    [Fact]
    public async Task ADeployThroughTheVirtualRepositoryLandsInTheDeployRepositoryOnly()
    {
        await PutAsync("v", $"{Folder}/3.0/lib-3.0.pom", Bytes(MavenPom.Generate("org.acme", "lib", "3.0", "jar", "Through virtual", "d")));
        await PutAsync("v", $"{Folder}/3.0/lib-3.0.jar", Bytes("through virtual"));

        Assert.Equal((1, 1), await StoredAsync("loc"));
        Assert.Equal((0, 0), await StoredAsync("up"));
        Assert.Equal("through virtual", await TextAsync("v", $"{Folder}/3.0/lib-3.0.jar"));
        Assert.Equal("through virtual", await TextAsync("loc", $"{Folder}/3.0/lib-3.0.jar"));
        Assert.DoesNotContain(_upstream.Requests, r => r.Method == "PUT");
    }

    [Fact]
    public async Task ADeleteThroughTheVirtualRepositoryDeletesInTheDeployRepository()
    {
        await DeployReleaseAsync("loc", "1.0");

        using var response = await SendAsync(HttpMethod.Delete, "v", $"{Folder}/1.0/lib-1.0.jar", key: Key);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("loc", $"{Folder}/1.0/lib-1.0.jar"));
    }

    [Fact]
    public async Task AVirtualRepositoryWithoutADeployRepositoryTakesNoChanges()
    {
        await CreateVirtualAsync("readonly", new[] { "loc", "up" }, deploy: null);

        using var response = await SendAsync(HttpMethod.Put, "readonly", $"{Folder}/3.0/lib-3.0.jar", Bytes("x"), Key);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal((0, 0), await StoredAsync("loc"));
    }

    // ---- pages and the preview ----

    [Fact]
    public async Task TheArtifactPageMergesTheMembersVersionsAndTheBrowsePageListsTheirArtifacts()
    {
        await DeployReleaseAsync("loc", "1.0");
        _upstream.AddRelease("org.acme", "lib", "2.0");
        (await SendAsync(HttpMethod.Get, "up", $"{Folder}/2.0/lib-2.0.pom")).Dispose();

        using var scope = await _app.CreateRepositoryScopeAsync("v", Cancel);
        var maven = scope.ServiceProvider.GetRequiredService<IMavenRepository>();
        var artifact = await maven.GetArtifactAsync("org.acme", "lib", Cancel);
        var search = await maven.SearchAsync(null, 0, 10, null, Cancel);

        // 2.0 is only in the remote member once something was requested through it (the pages list what is stored).
        Assert.Equal(new[] { "2.0", "1.0" }, artifact!.Versions.Select(v => v.Version));
        Assert.Equal(1, search.Total);
        Assert.Equal("org.acme:lib", search.Hits.Single().CoordinateName);
        Assert.NotNull(await maven.GetVersionAsync("org.acme", "lib", "2.0", Cancel));

        using var browse = await _client.GetAsync("/r/v/maven-artifacts", Cancel);
        var html = await browse.Content.ReadAsStringAsync(Cancel);
        Assert.Equal(HttpStatusCode.OK, browse.StatusCode);
        Assert.Contains("merges its member repositories", html);
        Assert.Contains("org.acme:lib", html);
    }

    [Fact]
    public async Task ThePreviewNamesTheMemberAMavenVersionComesFromWhateverItsSpelling()
    {
        await DeployReleaseAsync("loc", "1.0");
        _upstream.AddRelease("org.acme", "lib", "5.3.20.RELEASE");

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>();

        var mine = await admin.PreviewAsync("v", "org.acme:lib", "1.0", Cancel);
        Assert.Equal("loc", mine.Answers!.Name);
        Assert.Equal(new[] { ResolutionOutcome.Stored, ResolutionOutcome.NotAsked }, mine.Steps.Select(s => s.Outcome));

        var theirs = await admin.PreviewAsync("v", "org.acme:lib", "5.3.20.RELEASE", Cancel);
        Assert.Equal("up", theirs.Answers!.Name);
        Assert.Equal("5.3.20.RELEASE", theirs.Version);
        Assert.Equal(new[] { ResolutionOutcome.NotFound, ResolutionOutcome.WouldFetch }, theirs.Steps.Select(s => s.Outcome));

        var nobody = await admin.PreviewAsync("v", "org.acme:lib", "9.9", Cancel);
        Assert.Null(nobody.Answers);

        await Assert.ThrowsAsync<ArgumentException>(() => admin.PreviewAsync("v", "lib", "1.0", Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => admin.PreviewAsync("v", "org.acme:lib", "1 0", Cancel));
        Assert.Equal(0, _upstream.Gets("lib-5.3.20.RELEASE.jar"));
    }
}
