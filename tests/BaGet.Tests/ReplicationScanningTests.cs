using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// A pull replication stores artifacts without going through the controller that checks the scanning policy, so the replicators must
/// apply the same deploy rules (NuGet: the dependency check before storing; Docker: the stored-image check, undone when refused).
/// </summary>
public class ReplicationScanningTests : IAsyncLifetime
{
    private const int High = 2, Critical = 3;

    private readonly ITestOutputHelper _output;
    private FakeOsvServer _osv = null!;
    private ReplicationHarness _h = null!;
    private FakeTrivyRunner _trivy = null!;

    public ReplicationScanningTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _osv = await FakeOsvServer.StartAsync();
        _trivy = new FakeTrivyRunner();
        _h = new ReplicationHarness(
            _output,
            new Dictionary<string, string>
            {
                { "Scanning:Enabled", "true" },
                { "Scanning:OsvUrl", _osv.Url },
                { "Scanning:RescanInterval", "00:00:00" },
                { "Scanning:ScanOnDeploy", "false" },
                { "Scanning:TrivyPath", "trivy" },
                { "Scanning:TrivyRegistryHost", "registry.test" },
            },
            services =>
            {
                services.RemoveAll<ITrivyRunner>();
                services.AddSingleton<ITrivyRunner>(_trivy);
            });
    }

    public async ValueTask DisposeAsync()
    {
        _h.Dispose();
        await _osv.DisposeAsync();
    }

    private async Task BlockDeploysAsync(string repository)
    {
        await _h.Local.WithAsync(async services =>
        {
            var key = (await services.GetRequiredService<IRepositoryService>().FindAsync(repository, Token))!.Key;
            await services.GetRequiredService<IScanPolicyService>()
                .SaveAsync(ScanPolicyScope.Repository, key, new ScanPolicyValues(null, null, High, false), "tester", Token);

            return true;
        });
    }

    [Fact]
    public async Task APullDoesNotStoreANuGetPackageTheDeployPolicyRefuses()
    {
        await _h.Peer.App.AddPackageAsync(TestPackages.Build("Vuln.Pkg", "1.0.0", dependencies: new[] { ("Newtonsoft.Json", "[9.0.1,)") }), Token);
        _osv.Add("NuGet", "Newtonsoft.Json", "9.0.1", "GHSA-json", "HIGH", "13.0.1");
        await BlockDeploysAsync(Repository.DefaultName);
        var key = await _h.CreateAsync(direction: ReplicationDirection.Pull);

        var run = await _h.RunAsync(key);

        Assert.Equal(1, run.Failures);
        Assert.Equal(0, run.Transferred);
        Assert.Contains("GHSA-json", run.Detail);
        Assert.Empty((await _h.InventoryAsync(_h.Local)).Items);
    }

    [Fact]
    public async Task APullStoresANuGetPackageTheDeployPolicyAccepts()
    {
        await _h.Peer.App.AddPackageAsync(TestPackages.Build("Clean.Pkg", "1.0.0", dependencies: new[] { ("Newtonsoft.Json", "[13.0.1,)") }), Token);
        await BlockDeploysAsync(Repository.DefaultName);
        var key = await _h.CreateAsync(direction: ReplicationDirection.Pull);

        var run = await _h.RunAsync(key);

        Assert.Equal(0, run.Failures);
        Assert.Equal(1, run.Transferred);
    }

    [Fact]
    public async Task APullRemovesADockerImageTheDeployPolicyRefuses()
    {
        await _h.Local.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        await _h.Peer.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        var bad = OciTestImage.Create("bad");
        await PushImageAsync(_h.Peer, "svc", "latest", bad);
        _trivy.Hits.Add(new ScanHit("CVE-2024-0005", "openssl", "1.1.1", Critical, "A flaw", "https://avd.example.test/CVE-2024-0005", "1.1.2"));
        await BlockDeploysAsync("img");
        var key = await _h.CreateAsync("d", "img", "img", ReplicationDirection.Pull);

        var run = await _h.RunAsync(key);

        Assert.Equal(1, run.Failures);
        using var scope = await _h.Local.App.CreateRepositoryScopeAsync("img", Token);
        var registry = scope.ServiceProvider.GetRequiredService<IOciRegistry>();
        Assert.Null(await registry.GetManifestAsync("svc", "latest", Token));
        Assert.Null(await registry.GetManifestAsync("svc", bad.ManifestDigest, Token));
    }

    [Fact]
    public async Task APullQueuesAScanForADockerImageTheDeployPolicyDoesNotRefuse()
    {
        await _h.Local.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        await _h.Peer.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        var image = OciTestImage.Create("fine");
        await PushImageAsync(_h.Peer, "svc", "latest", image);
        await BlockDeploysAsync("img");
        var key = await _h.CreateAsync("d", "img", "img", ReplicationDirection.Pull);

        var run = await _h.RunAsync(key);

        Assert.Equal(0, run.Failures);
        // Judged straight away, like a push: the result exists without waiting for a sweep.
        using var inner = await _h.Local.App.CreateRepositoryScopeAsync("img", Token);
        var result = await inner.ServiceProvider.GetRequiredService<IScanService>().FindAsync("svc", image.ManifestDigest, Token);
        Assert.NotNull(result?.ScannedAt);
    }

    [Fact]
    public async Task ARefusedSourceWinsReplacementKeepsTheLocalNpmVersion()
    {
        await _h.Local.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await _h.Peer.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        var local = NpmTestPackages.Tarball("pkg", "1.0.0", description: "local build");
        var bad = NpmTestPackages.Tarball("pkg", "1.0.0", description: "peer build", dependencies: new Dictionary<string, string> { ["lodash"] = "4.17.0" });
        await PublishNpmAsync(_h.Local, local);
        await PublishNpmAsync(_h.Peer, bad);
        _osv.Add("npm", "lodash", "4.17.0", "GHSA-lodash", "HIGH", "4.17.21");
        await BlockDeploysAsync("web");
        var key = await _h.CreateAsync("c", "web", "web", ReplicationDirection.Pull, conflict: ReplicationConflict.SourceWins);

        var run = await _h.RunAsync(key);

        Assert.Equal(1, run.Failures);
        Assert.Equal(0, run.Transferred);
        Assert.Equal(local.Integrity, (await _h.InventoryAsync(_h.Local, "web")).Items.Single().Digest);
    }

    [Fact]
    public async Task ARefusedSourceWinsReplacementKeepsTheLocalDockerTag()
    {
        await _h.Local.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        await _h.Peer.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        var good = OciTestImage.Create("good");
        var bad = OciTestImage.Create("bad");
        await PushImageAsync(_h.Local, "svc", "latest", good);
        await PushImageAsync(_h.Peer, "svc", "latest", bad);
        _trivy.Hits.Add(new ScanHit("CVE-2024-0005", "openssl", "1.1.1", Critical, "A flaw", "https://avd.example.test/CVE-2024-0005", "1.1.2"));
        await BlockDeploysAsync("img");
        var key = await _h.CreateAsync("d", "img", "img", ReplicationDirection.Pull, conflict: ReplicationConflict.SourceWins);

        var run = await _h.RunAsync(key);

        Assert.Equal(1, run.Failures);
        using var scope = await _h.Local.App.CreateRepositoryScopeAsync("img", Token);
        var registry = scope.ServiceProvider.GetRequiredService<IOciRegistry>();
        var kept = await registry.GetManifestAsync("svc", "latest", Token);
        Assert.NotNull(kept);
        Assert.Equal(good.ManifestDigest, kept!.Digest);
    }

    [Fact]
    public async Task ARefusedSourceWinsReplacementKeepsTheLocalMavenFile()
    {
        await _h.Local.App.CreateMavenRepositoryAsync("libs", cancellationToken: Token);
        await _h.Peer.App.CreateMavenRepositoryAsync("libs", cancellationToken: Token);
        const string path = "org/acme/lib/1.0/lib-1.0.pom";
        await DeployMavenAsync(_h.Local, "libs", path, Pom(null));
        await DeployMavenAsync(_h.Peer, "libs", path, Pom("<dependency><groupId>org.bad</groupId><artifactId>bad</artifactId><version>2.3.4</version></dependency>"));
        _osv.Add("Maven", "org.bad:bad", "2.3.4", "GHSA-maven-replace", "HIGH", "2.3.5");
        var before = (await _h.InventoryAsync(_h.Local, "libs")).Items.Single().Digest;
        await BlockDeploysAsync("libs");
        var key = await _h.CreateAsync("m", "libs", "libs", ReplicationDirection.Pull, conflict: ReplicationConflict.SourceWins);

        var run = await _h.RunAsync(key);

        Assert.Equal(1, run.Failures);
        Assert.Equal(0, run.Transferred);
        Assert.Contains("GHSA-maven-replace", run.Detail);
        Assert.Equal(before, (await _h.InventoryAsync(_h.Local, "libs")).Items.Single().Digest);
    }

    [Fact]
    public async Task ARefusedNuGetReplacingPullKeepsTheLocalPackage()
    {
        // NuGet items carry no digest, so the planner never asks for a replacement; the replicator's own contract is still that a
        // refused replacement keeps the stored package.
        await _h.Local.App.AddPackageAsync(TestPackages.Build("Same.Pkg", "1.0.0", dependencies: new[] { ("Newtonsoft.Json", "[13.0.1,)") }), Token);
        await _h.Peer.App.AddPackageAsync(TestPackages.Build("Same.Pkg", "1.0.0", dependencies: new[] { ("Newtonsoft.Json", "[9.0.1,)") }), Token);
        _osv.Add("NuGet", "Newtonsoft.Json", "9.0.1", "GHSA-json-replace", "HIGH", "13.0.1");
        await BlockDeploysAsync(Repository.DefaultName);

        var refused = await _h.Local.WithAsync(async services =>
        {
            var replicator = services.GetFormatService<IFormatReplicator>(RepositoryFormat.NuGet)!;
            using var peer = services.GetRequiredService<IReplicationPeerFactory>().Create(ReplicationHarness.PeerAddress, Repository.DefaultName, _h.PeerKey);
            var item = (await replicator.ListAsync(Token)).Single();

            return await Assert.ThrowsAsync<ReplicationItemException>(() => replicator.PullReplacingAsync(item, peer, Token));
        });

        Assert.Contains("GHSA-json-replace", refused.Message);
        Assert.Single((await _h.InventoryAsync(_h.Local)).Items);
    }

    [Fact]
    public async Task ASourceWinsPushMovesADockerTagWithoutDeletingAtThePeerFirst()
    {
        await _h.Local.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        await _h.Peer.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        var local = OciTestImage.Create("local");
        await PushImageAsync(_h.Local, "svc", "latest", local);
        await PushImageAsync(_h.Peer, "svc", "latest", OciTestImage.Create("peer"));
        var key = await _h.CreateAsync("d", "img", "img", ReplicationDirection.Push, conflict: ReplicationConflict.SourceWins);

        var run = await _h.RunAsync(key);

        Assert.True(run.Failures == 0, run.Detail);
        Assert.Equal(1, run.Transferred);
        Assert.Equal(local.ManifestDigest, (await _h.InventoryAsync(_h.Peer, "img")).Items.Single().Digest);
        lock (_h.PeerRequests) Assert.DoesNotContain(_h.PeerRequests, r => r.StartsWith("DELETE ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePeerKeyIsNeverSentToARedirectTarget()
    {
        using var target = new Listener(_ => (200, null));
        using var origin = new Listener(_ => (302, target.Url + "stolen"));
        using var http = new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler { AllowAutoRedirect = false });
        using var peer = new ReplicationPeer(http, new Uri(origin.Url), "default", ownsClient: false, apiKey: "peer-secret-key");

        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, peer.Feed("v3/index.json"));
        peer.AddCredentials(request);
        using var response = await peer.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("peer-secret-key", Assert.Single(origin.Seen).Key);
        Assert.Null(Assert.Single(target.Seen).Key);
    }

    [Fact]
    public async Task ThePeerKeyFollowsARedirectWithinThePeersOwnOrigin()
    {
        using var origin = new Listener(path => path.EndsWith("/moved", StringComparison.Ordinal) ? (200, null) : (302, "moved"));
        using var http = new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler { AllowAutoRedirect = false });
        using var peer = new ReplicationPeer(http, new Uri(origin.Url), "default", ownsClient: false, apiKey: "peer-secret-key");

        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, peer.Feed("v3/index.json"));
        peer.AddCredentials(request);
        using var response = await peer.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, origin.Seen.Count);
        Assert.All(origin.Seen, s => Assert.Equal("peer-secret-key", s.Key));
    }

    [Fact]
    public async Task APeerBuiltByTheFactoryDoesNotFollowRedirectsItself()
    {
        using var target = new Listener(_ => (200, null));
        using var origin = new Listener(_ => (302, target.Url + "x"));
        using var peer = new ReplicationPeerFactory(new RemoteHttpClientFactory()).Create(origin.Url, "default", "peer-secret-key");

        // A GET carrying the key header: the factory's client must not follow the redirect on its own (the manual follow strips the key).
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, peer.Feed("v3/index.json"));
        peer.AddCredentials(request);
        using var response = await peer.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(target.Seen).Key);
        Assert.Null(target.Seen[0].Authorization);
    }

    /// <summary>A loopback HTTP server that records the key header and the Authorization header of each request.</summary>
    private sealed class Listener : IDisposable
    {
        private readonly System.Net.HttpListener _listener = new();
        private readonly Func<string, (int Status, string? Location)> _respond;

        public Listener(Func<string, (int Status, string? Location)> respond)
        {
            _respond = respond;
            int port;
            for (var attempt = 0; ; attempt++)
            {
                var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                probe.Start();
                port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                try
                {
                    _listener.Prefixes.Clear();
                    _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    _listener.Start();
                    break;
                }
                catch (System.Net.HttpListenerException) when (attempt < 5)
                {
                }
            }

            Url = $"http://127.0.0.1:{port}/";
            _ = Task.Run(LoopAsync);
        }

        public string Url { get; }

        public List<(string? Key, string? Authorization)> Seen { get; } = new();

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                System.Net.HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                lock (Seen) Seen.Add((context.Request.Headers["X-NuGet-ApiKey"], context.Request.Headers["Authorization"]));
                var (status, location) = _respond(context.Request.Url!.AbsolutePath);
                context.Response.StatusCode = status;
                if (location != null) context.Response.RedirectLocation = location;
                context.Response.Close();
            }
        }

        public void Dispose() => _listener.Close();
    }

    private static string Pom(string? dependency) => $"""
        <project>
          <modelVersion>4.0.0</modelVersion>
          <groupId>org.acme</groupId>
          <artifactId>lib</artifactId>
          <version>1.0</version>
          <dependencies>{dependency}</dependencies>
        </project>
        """;

    private static async Task DeployMavenAsync(AdminApiHarness side, string repository, string path, string content)
    {
        using var scope = await side.App.CreateRepositoryScopeAsync(repository, Token);
        var maven = scope.ServiceProvider.GetRequiredService<MavenRepositoryService>();
        var result = await maven.PutAsync(BaGetPrincipal.Superuser, MavenNames.ParsePath(path)!, new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)), Token);
        Assert.True(result.Succeeded, result.Message);
    }

    private static async Task PublishNpmAsync(AdminApiHarness side, NpmTarballFile tarball)
    {
        using var request = NpmTestPackages.Json(System.Net.Http.HttpMethod.Put, NpmTestPackages.Path("/r/web/npm/", tarball.Name), NpmTestPackages.PublishBody(tarball), AdminApiHarness.GlobalKey);
        using var response = await side.Client.SendAsync(request, Token);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task PushImageAsync(AdminApiHarness side, string image, string tag, OciTestImage built)
    {
        using var scope = await side.App.CreateRepositoryScopeAsync("img", Token);
        var registry = scope.ServiceProvider.GetRequiredService<IOciRegistry>();
        await registry.PutBlobAsync(image, built.ConfigDigest, new System.IO.MemoryStream(built.Config), Token);
        for (var i = 0; i < built.Layers.Count; i++) await registry.PutBlobAsync(image, built.LayerDigests[i], new System.IO.MemoryStream(built.Layers[i]), Token);
        await registry.PutManifestAsync(image, tag, OciTestImage.ManifestType, built.Manifest, Token);
    }
}
