using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Replication (P9) between two real servers in one process, one format at a time through the format's own protocol: push and pull,
/// skip of what the target holds, conflicts, delete propagation, interruption and resume, an unreachable peer, the deploy trigger and
/// the protection of the peer's key. The peer is another BaGet application reached through its HTTP pipeline.
/// </summary>
public class ReplicationTests : IDisposable
{
    private readonly ReplicationHarness _h;
    private readonly ITestOutputHelper _output;

    public ReplicationTests(ITestOutputHelper output)
    {
        _output = output;
        _h = new ReplicationHarness(output);
    }

    public void Dispose() => _h.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---- seeding ----

    private static Task AddNuGetAsync(AdminApiHarness side, string id, string version, string? repository = null)
        => side.App.AddPackageAsync(TestPackages.Build(id, version), Token, repository);

    private static async Task<NpmTarballFile> PublishNpmAsync(AdminApiHarness side, string repository, string name, string version, string tag = "latest")
    {
        var tarball = NpmTestPackages.Tarball(name, version);
        using var request = NpmTestPackages.Json(HttpMethod.Put, NpmTestPackages.Path($"/r/{repository}/npm/", name), NpmTestPackages.PublishBody(tarball, tag), AdminApiHarness.GlobalKey);
        using var response = await side.Client.SendAsync(request, Token);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}");

        return tarball;
    }

    private static async Task DeployMavenAsync(AdminApiHarness side, string repository, string path, string content)
    {
        using var scope = await side.App.CreateRepositoryScopeAsync(repository, Token);
        var maven = scope.ServiceProvider.GetRequiredService<MavenRepositoryService>();
        var result = await maven.PutAsync(BaGetPrincipal.Superuser, MavenNames.ParsePath(path)!, new MemoryStream(Encoding.UTF8.GetBytes(content)), Token);
        Assert.True(result.Succeeded, result.Message);
    }

    private static async Task PushImageAsync(AdminApiHarness side, string repository, string image, string tag, OciTestImage built)
    {
        using var scope = await side.App.CreateRepositoryScopeAsync(repository, Token);
        var registry = scope.ServiceProvider.GetRequiredService<IOciRegistry>();
        await registry.PutBlobAsync(image, built.ConfigDigest, new MemoryStream(built.Config), Token);
        for (var i = 0; i < built.Layers.Count; i++) await registry.PutBlobAsync(image, built.LayerDigests[i], new MemoryStream(built.Layers[i]), Token);
        await registry.PutManifestAsync(image, tag, OciTestImage.ManifestType, built.Manifest, Token);
    }

    private async Task<HashSet<string>> NamesAsync(AdminApiHarness side, string repository = "default")
        => (await _h.InventoryAsync(side, repository)).Items.Select(i => i.ToString()).ToHashSet();

    // ---- NuGet ----

    [Fact]
    public async Task APushCopiesAMissingPackageAndTheNextRunSkipsIt()
    {
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.1.0");
        var key = await _h.CreateAsync();

        var first = await _h.RunAsync(key);

        Assert.Equal(ReplicationRunStatus.Succeeded, first.Status);
        Assert.Equal(2, first.Transferred);
        Assert.True(first.Bytes > 0);
        Assert.Equal(new HashSet<string> { "Contoso.Lib 1.0.0", "Contoso.Lib 1.1.0" }, await NamesAsync(_h.Peer), StringComparer.OrdinalIgnoreCase);

        var second = await _h.RunAsync(key);
        Assert.Equal(ReplicationRunStatus.Succeeded, second.Status);
        Assert.Equal(0, second.Transferred);
        Assert.Equal(2, second.Skipped);
    }

    [Fact]
    public async Task APullStoresThePeersPackagesLocallyThroughTheNormalPipeline()
    {
        await AddNuGetAsync(_h.Peer, "Fabrikam.Core", "2.0.0");
        var key = await _h.CreateAsync(direction: ReplicationDirection.Pull);

        var run = await _h.RunAsync(key);

        Assert.Equal(ReplicationRunStatus.Succeeded, run.Status);
        Assert.Equal(1, run.Transferred);
        Assert.Contains("Fabrikam.Core 2.0.0", await NamesAsync(_h.Local), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APackageHeldOnBothSidesIsNotACopyEvenWithDifferentCaseOfTheId()
    {
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        await AddNuGetAsync(_h.Peer, "contoso.lib", "1.0.0");
        var key = await _h.CreateAsync();

        var run = await _h.RunAsync(key);

        Assert.Equal(0, run.Transferred);
        Assert.Equal(1, run.Skipped);
        Assert.Equal(0, run.Failures);
    }

    [Fact]
    public async Task ReplicationIntoANamedRepositoryOfThePeerPrefixesItsRoutes()
    {
        await _h.Peer.App.CreateRepositoryAsync("mirror", Token);
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        var key = await _h.CreateAsync(peerRepository: "mirror");

        var run = await _h.RunAsync(key);

        Assert.Equal(ReplicationRunStatus.Succeeded, run.Status);
        Assert.Contains("Contoso.Lib 1.0.0", await NamesAsync(_h.Peer, "mirror"), StringComparer.OrdinalIgnoreCase);
        Assert.Empty((await _h.InventoryAsync(_h.Peer)).Items);
    }

    // ---- npm ----

    [Fact]
    public async Task NpmVersionsAndTheirTagsTravelInBothDirections()
    {
        await _h.Local.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await _h.Peer.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await PublishNpmAsync(_h.Local, "web", "left-pad", "1.0.0");
        await PublishNpmAsync(_h.Local, "web", "left-pad", "2.0.0", tag: "next");
        await PublishNpmAsync(_h.Peer, "web", "@acme/tool", "3.0.0");

        var push = await _h.RunAsync(await _h.CreateAsync("push", "web", "web"));
        var pull = await _h.RunAsync(await _h.CreateAsync("pull", "web", "web", ReplicationDirection.Pull));

        Assert.Equal(ReplicationRunStatus.Succeeded, push.Status);
        Assert.Equal(2, push.Transferred);
        Assert.Equal(ReplicationRunStatus.Succeeded, pull.Status);
        Assert.Equal(1, pull.Transferred);
        Assert.Contains("left-pad 2.0.0", await NamesAsync(_h.Peer, "web"));
        Assert.Contains("@acme/tool 3.0.0", await NamesAsync(_h.Local, "web"));

        // The tag that named 2.0.0 reached the peer with it.
        using var scope = await _h.Peer.App.CreateRepositoryScopeAsync("web", Token);
        var package = await scope.ServiceProvider.GetRequiredService<NpmRegistryService>().GetPackageAsync("left-pad", Token);
        Assert.Equal("2.0.0", package!.DistTags["next"]);
    }

    [Fact]
    public async Task ANpmVersionWithDifferentContentOnBothSidesIsAConflictAndTheTargetKeepsItsOwn()
    {
        await _h.Local.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await _h.Peer.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        var local = NpmTestPackages.Tarball("pkg", "1.0.0", description: "local build");
        var remote = NpmTestPackages.Tarball("pkg", "1.0.0", description: "peer build");
        Assert.NotEqual(local.Integrity, remote.Integrity);
        await PublishNpmBytesAsync(_h.Local, "web", local);
        await PublishNpmBytesAsync(_h.Peer, "web", remote);
        var key = await _h.CreateAsync("c", "web", "web");

        var run = await _h.RunAsync(key);

        Assert.Equal(ReplicationRunStatus.Succeeded, run.Status);
        Assert.Equal(1, run.Conflicts);
        Assert.Equal(0, run.Transferred);
        Assert.Contains("conflict", run.Detail);
        Assert.Equal(remote.Integrity, (await _h.InventoryAsync(_h.Peer, "web")).Items.Single().Digest);
    }

    [Fact]
    public async Task WithSourceWinsTheSourcesNpmVersionReplacesTheTargets()
    {
        await _h.Local.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await _h.Peer.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        var local = NpmTestPackages.Tarball("pkg", "1.0.0", description: "local build");
        var remote = NpmTestPackages.Tarball("pkg", "1.0.0", description: "peer build");
        await PublishNpmBytesAsync(_h.Local, "web", local);
        await PublishNpmBytesAsync(_h.Peer, "web", remote);
        var key = await _h.CreateAsync("c", "web", "web", conflict: ReplicationConflict.SourceWins);

        var run = await _h.RunAsync(key);

        Assert.True(run.Failures == 0, run.Detail);
        Assert.Equal(1, run.Conflicts);
        Assert.Equal(1, run.Transferred);
        Assert.Equal(local.Integrity, (await _h.InventoryAsync(_h.Peer, "web")).Items.Single().Digest);
    }

    private static async Task PublishNpmBytesAsync(AdminApiHarness side, string repository, NpmTarballFile tarball)
    {
        using var request = NpmTestPackages.Json(HttpMethod.Put, NpmTestPackages.Path($"/r/{repository}/npm/", tarball.Name), NpmTestPackages.PublishBody(tarball), AdminApiHarness.GlobalKey);
        using var response = await side.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- Maven ----

    [Fact]
    public async Task MavenFilesAreReplicatedOneByOneWithTheirSha256()
    {
        await _h.Local.App.CreateMavenRepositoryAsync("libs", cancellationToken: Token);
        await _h.Peer.App.CreateMavenRepositoryAsync("libs", cancellationToken: Token);
        await DeployMavenAsync(_h.Local, "libs", "org/acme/lib/1.0/lib-1.0.jar", "jar bytes");
        await DeployMavenAsync(_h.Local, "libs", "org/acme/lib/1.0/lib-1.0.pom", "<project/>");
        await DeployMavenAsync(_h.Peer, "libs", "org/acme/other/2.0/other-2.0.jar", "other jar");

        var push = await _h.RunAsync(await _h.CreateAsync("push", "libs", "libs"));
        var pull = await _h.RunAsync(await _h.CreateAsync("pull", "libs", "libs", ReplicationDirection.Pull));

        Assert.True(push.Failures == 0, push.Detail);
        Assert.Equal(2, push.Transferred);
        Assert.True(pull.Failures == 0, pull.Detail);
        Assert.Equal(1, pull.Transferred);
        var peer = await _h.InventoryAsync(_h.Peer, "libs");
        var jar = peer.Items.Single(i => i.Part == "lib-1.0.jar");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("jar bytes"))).ToLowerInvariant(), jar.Digest);
        Assert.Equal(3, (await _h.InventoryAsync(_h.Local, "libs")).Count);
    }

    // ---- Docker ----

    [Fact]
    public async Task ADockerTagCopiesItsManifestAndLayersAndOnlyTheMissingOnesTheSecondTime()
    {
        await _h.Local.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        await _h.Peer.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        var one = OciTestImage.Create("one", layers: 2);
        await PushImageAsync(_h.Local, "img", "app/web", "1.0", one);
        var key = await _h.CreateAsync("d", "img", "img");

        var first = await _h.RunAsync(key);

        Assert.True(first.Failures == 0, first.Detail);
        Assert.Equal(1, first.Transferred);
        var tag = (await _h.InventoryAsync(_h.Peer, "img")).Items.Single();
        Assert.Equal(("app/web", "1.0", one.ManifestDigest), (tag.Name, tag.Version, tag.Digest));
        using (var scope = await _h.Peer.App.CreateRepositoryScopeAsync("img", Token))
        {
            var registry = scope.ServiceProvider.GetRequiredService<IOciRegistry>();
            Assert.NotNull(await registry.GetBlobInfoAsync("app/web", one.LayerDigests[1], Token));
        }

        // A second tag sharing the layers sends no blob at all.
        var two = OciTestImage.Create("one", layers: 2);
        await PushImageAsync(_h.Local, "img", "app/web", "1.1", two);
        _h.PeerRequests.Clear();
        var second = await _h.RunAsync(key);
        Assert.Equal(1, second.Transferred);
        Assert.DoesNotContain(_h.PeerRequests, r => r.StartsWith("POST", StringComparison.Ordinal) && r.Contains("/blobs/uploads/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADockerTagIsPulledIntoALocalRepositoryWithItsBlobs()
    {
        await _h.Local.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        await _h.Peer.App.CreateDockerRepositoryAsync("img", cancellationToken: Token);
        var built = OciTestImage.Create("pulled", layers: 3, layerSize: 200_000);
        await PushImageAsync(_h.Peer, "img", "svc", "latest", built);
        var key = await _h.CreateAsync("d", "img", "img", ReplicationDirection.Pull);

        var run = await _h.RunAsync(key);

        Assert.True(run.Failures == 0, run.Detail);
        Assert.Equal(1, run.Transferred);
        Assert.True(run.Bytes > 600_000);
        using var scope = await _h.Local.App.CreateRepositoryScopeAsync("img", Token);
        var registry = scope.ServiceProvider.GetRequiredService<IOciRegistry>();
        var manifest = await registry.GetManifestAsync("svc", "latest", Token);
        Assert.Equal(built.ManifestDigest, manifest!.Digest);
        foreach (var digest in built.LayerDigests) Assert.NotNull(await registry.GetBlobInfoAsync("svc", digest, Token));
    }

    // ---- deletes ----

    [Fact]
    public async Task WhatWasCopiedAndLaterDeletedAtTheSourceIsDeletedAtTheTargetButNothingElse()
    {
        await _h.Local.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await _h.Peer.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await PublishNpmAsync(_h.Local, "web", "copied", "1.0.0");
        await PublishNpmAsync(_h.Peer, "web", "peer-only", "1.0.0");
        var key = await _h.CreateAsync("d", "web", "web", propagateDeletes: true);
        await _h.RunAsync(key);
        Assert.Contains("copied 1.0.0", await NamesAsync(_h.Peer, "web"));

        using (var scope = await _h.Local.App.CreateRepositoryScopeAsync("web", Token))
        {
            await scope.ServiceProvider.GetRequiredService<NpmRegistryService>().PurgeAsync("copied", "1.0.0", Token);
        }

        var run = await _h.RunAsync(key);

        Assert.True(run.Failures == 0, run.Detail);
        Assert.Equal(1, run.Deleted);
        var peer = await NamesAsync(_h.Peer, "web");
        Assert.DoesNotContain("copied 1.0.0", peer);
        Assert.Contains("peer-only 1.0.0", peer);
    }

    [Fact]
    public async Task WithoutPropagationADeleteAtTheSourceLeavesTheTargetAlone()
    {
        await _h.Local.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await _h.Peer.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await PublishNpmAsync(_h.Local, "web", "copied", "1.0.0");
        var key = await _h.CreateAsync("d", "web", "web");
        await _h.RunAsync(key);
        using (var scope = await _h.Local.App.CreateRepositoryScopeAsync("web", Token))
        {
            await scope.ServiceProvider.GetRequiredService<NpmRegistryService>().PurgeAsync("copied", "1.0.0", Token);
        }

        var run = await _h.RunAsync(key);

        Assert.Equal(0, run.Deleted);
        Assert.Contains("copied 1.0.0", await NamesAsync(_h.Peer, "web"));
    }

    // ---- interruption ----

    [Fact]
    public async Task AnInterruptedRunKeepsWhatItCopiedAndTheNextRunContinuesFromThere()
    {
        for (var i = 1; i <= 5; i++) await AddNuGetAsync(_h.Local, "Contoso.Lib", $"1.0.{i}");
        var key = await _h.CreateAsync();

        using var stop = new CancellationTokenSource();
        var first = await _h.RunAsync(key, afterItem: (handled, _) =>
        {
            if (handled == 2) stop.Cancel();
            return Task.CompletedTask;
        }, stop: stop);

        Assert.Equal(ReplicationRunStatus.Interrupted, first.Status);
        Assert.Equal(2, first.Transferred);
        Assert.Equal(2, (await _h.InventoryAsync(_h.Peer)).Count);

        var resumed = await _h.RunAsync(key, "resume", first.Key);

        Assert.Equal(ReplicationRunStatus.Succeeded, resumed.Status);
        Assert.Equal(first.Key, resumed.ResumedFrom);
        Assert.Equal(3, resumed.Transferred);
        Assert.Equal(2, resumed.Skipped);
        Assert.Equal(5, (await _h.InventoryAsync(_h.Peer)).Count);
        Assert.Equal(2, (await _h.RunsAsync(key)).Count);
    }

    [Fact]
    public async Task AnUnreachablePeerFailsTheRunWithoutCountingEveryItemAsFailedAndALaterRunConverges()
    {
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.1");
        var key = await _h.CreateAsync();

        _h.FailPeer = true;
        var down = await _h.RunAsync(key);

        Assert.Equal(ReplicationRunStatus.Failed, down.Status);
        Assert.Contains("could not be reached", down.Error);
        Assert.Equal(0, down.Failures);
        Assert.Equal(0, down.Transferred);

        _h.FailPeer = false;
        var up = await _h.RunAsync(key, "resume", down.Key);
        Assert.Equal(ReplicationRunStatus.Succeeded, up.Status);
        Assert.Equal(2, up.Transferred);
    }

    [Fact]
    public async Task APeerThatRefusesTheKeyIsReportedWithoutEchoingIt()
    {
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        _h.KeyOverride = "not-the-key";
        var key = await _h.CreateAsync();

        var run = await _h.RunAsync(key);

        Assert.Equal(ReplicationRunStatus.Failed, run.Status);
        Assert.Contains("refused the API key", run.Error);
        Assert.DoesNotContain("not-the-key", run.Error + run.Detail);
    }

    [Fact]
    public async Task OneBadItemIsLoggedAndTheRestOfTheRunGoesOn()
    {
        await _h.Local.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await _h.Peer.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await PublishNpmAsync(_h.Local, "web", "good", "1.0.0");
        await PublishNpmAsync(_h.Local, "web", "bad", "1.0.0");
        await PublishNpmAsync(_h.Local, "web", "fine", "1.0.0");

        // The tarball of one version vanishes from the store after it was listed.
        using (var scope = await _h.Local.App.CreateRepositoryScopeAsync("web", Token))
        {
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
            await storage.DeleteAsync("repos/web/npm/bad/-/bad-1.0.0.tgz", Token);
        }

        var run = await _h.RunAsync(await _h.CreateAsync("p", "web", "web"));

        Assert.Equal(ReplicationRunStatus.Failed, run.Status);
        Assert.Equal(1, run.Failures);
        Assert.Equal(2, run.Transferred);
        Assert.Contains("bad 1.0.0", run.Detail);
    }

    // ---- rules ----

    [Fact]
    public async Task ADisabledReplicationCannotBeRun()
    {
        var key = await _h.CreateAsync(enabled: false);

        var e = await Assert.ThrowsAsync<ReplicationException>(() => _h.RunAsync(key));

        Assert.Contains("disabled", e.Message);
    }

    [Fact]
    public async Task OnlyALocalRepositoryOfTheSameFormatMayBeReplicated()
    {
        await _h.Local.App.CreateNpmRepositoryAsync("web", cancellationToken: Token);
        await _h.Peer.App.CreateRepositoryAsync("nuget2", Token);
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");

        var wrongFormat = await _h.RunAsync(await _h.CreateAsync("f", "web", "nuget2"));
        Assert.Equal(ReplicationRunStatus.Failed, wrongFormat.Status);
        Assert.Contains("NuGet repository", wrongFormat.Error);

    }

    [Fact]
    public async Task APushIntoARepositoryThatCannotReceiveIsRefused()
    {
        await _h.Peer.WithAsync(async services =>
        {
            await services.GetRequiredService<IRepositoryService>().CreateAsync(
                "proxy", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "https://api.nuget.org/v3/index.json" }, Token);
            return 0;
        });
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");

        var run = await _h.RunAsync(await _h.CreateAsync("p", "default", "proxy"));

        Assert.Equal(ReplicationRunStatus.Failed, run.Status);
        Assert.Contains("cannot receive", run.Error);
    }

    [Fact]
    public async Task TheLedgerIsClearedWhenTheRelationshipChanges()
    {
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        var key = await _h.CreateAsync();
        await _h.RunAsync(key);

        Assert.Equal(1, await _h.Local.WithAsync(async s => await s.GetRequiredService<IContext>().ReplicationItems!.CountAsync(Token)));

        await _h.Peer.App.CreateRepositoryAsync("other", Token);
        await _h.Local.WithAsync(async s => await s.GetRequiredService<IReplicationAdministration>().SaveAsync(
            new ReplicationInput(key, "r1", "default", ReplicationDirection.Push, ReplicationHarness.PeerAddress, "other", null, false, true, false, null, false, ReplicationConflict.TargetWins), "root", Token));

        Assert.Equal(0, await _h.Local.WithAsync(async s => await s.GetRequiredService<IContext>().ReplicationItems!.CountAsync(Token)));
    }

    // ---- jobs, worker, lease ----

    [Fact]
    public async Task AJobHoldsTheLeaseAndASecondStartIsRefusedWhileItRuns()
    {
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        var key = await _h.CreateAsync();
        var jobs = _h.Local.App.Services.GetRequiredService<ReplicationJobs>();
        var lease = _h.Local.App.Services.GetRequiredService<IJobLease>();

        await using (await lease.TryAcquireAsync(ReplicationJobs.LeaseName(key), Token))
        {
            Assert.True(jobs.TryStart(key, "manual", "root", null, out _));
            await jobs.WaitAsync(key);
            Assert.Equal("Another node is running this replication.", jobs.Get(key)!.Refusal);
        }

        Assert.Empty((await _h.InventoryAsync(_h.Peer)).Items);

        Assert.True(jobs.TryStart(key, "manual", "root", null, out _));
        await jobs.WaitAsync(key);
        Assert.Single((await _h.InventoryAsync(_h.Peer)).Items);
        Assert.Null(jobs.Get(key)!.Refusal);
    }

    [Fact]
    public async Task TheWorkerStartsADueScheduleAndLeavesAnotherForLater()
    {
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        var key = await _h.CreateAsync(scheduleMinutes: 5);
        var worker = _h.Local.App.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ReplicationWorker>().Single();

        Assert.False(ReplicationWorker.IsDue(new ReplicationConfig { Enabled = true, ScheduleMinutes = 5, Created = DateTime.UtcNow }, DateTime.UtcNow));
        Assert.True(ReplicationWorker.IsDue(new ReplicationConfig { Enabled = true, ScheduleMinutes = 5, Created = DateTime.UtcNow.AddMinutes(-6) }, DateTime.UtcNow));
        Assert.False(ReplicationWorker.IsDue(new ReplicationConfig { Enabled = false, ScheduleMinutes = 5, Created = DateTime.UtcNow.AddHours(-1) }, DateTime.UtcNow));

        // Not yet due: the schedule counts from the moment it was set.
        Assert.Empty(await worker.RunDueAsync(TimeSpan.Zero, Token));

        await _h.Local.WithAsync(async s =>
        {
            var config = await s.GetRequiredService<IContext>().ReplicationConfigs!.SingleAsync(Token);
            var now = s.GetRequiredService<SystemTime>().UtcNow; // the harness fixes the clock
            config.ScheduleStart = now.AddMinutes(-10);
            config.Created = now.AddMinutes(-10);
            await s.GetRequiredService<IContext>().SaveChangesAsync(Token);
            return 0;
        });

        var started = await worker.RunDueAsync(TimeSpan.Zero, Token);
        Assert.Equal(new[] { key }, started);
        await _h.Local.App.Services.GetRequiredService<ReplicationJobs>().WaitAsync(key);
        Assert.Single((await _h.InventoryAsync(_h.Peer)).Items);
        Assert.Equal("schedule", (await _h.RunsAsync(key)).Single().Trigger);
    }

    [Fact]
    public async Task ADeployToTheLocalRepositoryStartsAPushOnceItIsQuiet()
    {
        var key = await _h.CreateAsync(onDeploy: true);
        var worker = _h.Local.App.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ReplicationWorker>().Single();
        Assert.Empty(await worker.RunDueAsync(TimeSpan.Zero, Token));

        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");

        // Still inside the quiet period: nothing starts yet, and the news is kept.
        Assert.Empty(await worker.RunDueAsync(TimeSpan.FromHours(1), Token));
        var started = await worker.RunDueAsync(TimeSpan.Zero, Token);

        Assert.Equal(new[] { key }, started);
        await _h.Local.App.Services.GetRequiredService<ReplicationJobs>().WaitAsync(key);
        Assert.Single((await _h.InventoryAsync(_h.Peer)).Items);
        Assert.Equal("deploy", (await _h.RunsAsync(key)).Single().Trigger);
    }

    [Fact]
    public async Task ADeployNoticeIsKeptWhenAnotherNodeHoldsTheLeaseSoTheNextPassPushesIt()
    {
        var key = await _h.CreateAsync(onDeploy: true);
        var worker = _h.Local.App.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ReplicationWorker>().Single();
        var jobs = _h.Local.App.Services.GetRequiredService<ReplicationJobs>();
        var lease = _h.Local.App.Services.GetRequiredService<IJobLease>();

        await using (await lease.TryAcquireAsync(ReplicationJobs.LeaseName(key), Token))
        {
            await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");

            // Started here, but another node holds the lease: nothing was pushed and the news must survive.
            Assert.Equal(new[] { key }, await worker.RunDueAsync(TimeSpan.Zero, Token));
            await jobs.WaitAsync(key);
            Assert.Equal("Another node is running this replication.", jobs.Get(key)!.Refusal);
        }

        Assert.Equal(new[] { key }, await worker.RunDueAsync(TimeSpan.Zero, Token));
        await jobs.WaitAsync(key);
        Assert.Single((await _h.InventoryAsync(_h.Peer)).Items);
    }

    [Fact]
    public async Task ADeployToAnotherRepositoryDoesNotStartThePush()
    {
        await _h.Local.App.CreateRepositoryAsync("other", Token);
        await _h.CreateAsync(onDeploy: true);
        var worker = _h.Local.App.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ReplicationWorker>().Single();

        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0", repository: "other");

        Assert.Empty(await worker.RunDueAsync(TimeSpan.Zero, Token));
    }

    [Fact]
    public async Task ARunLeftRunningByAStoppedProcessIsMarkedInterruptedByTheWorker()
    {
        var key = await _h.CreateAsync();
        await _h.Local.WithAsync(async s =>
        {
            var context = s.GetRequiredService<IContext>();
            context.ReplicationRuns!.Add(new ReplicationRun { ConfigKey = key, Status = ReplicationRunStatus.Running, Trigger = "manual", Started = DateTime.UtcNow });
            await context.SaveChangesAsync(Token);
            return 0;
        });
        var worker = _h.Local.App.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ReplicationWorker>().Single();

        await worker.RunDueAsync(TimeSpan.Zero, Token);

        var run = (await _h.RunsAsync(key)).Single();
        Assert.Equal(ReplicationRunStatus.Interrupted, run.Status);
        Assert.NotNull(run.Finished);
    }

    [Fact]
    public async Task TheMaintenanceGateRefusesARun()
    {
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0");
        var key = await _h.CreateAsync();
        var gate = _h.Local.App.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("test");
        try
        {
            await Assert.ThrowsAsync<MaintenanceException>(() => _h.RunAsync(key));
            Assert.Throws<MaintenanceException>(() => _h.Local.App.Services.GetRequiredService<ReplicationJobs>().TryStart(key, "manual", "root", null, out _));
        }
        finally
        {
            gate.Open();
        }
    }

    // ---- the peer's key ----

    [Fact]
    public async Task ThePeerKeyIsStoredProtectedAndNeverReturned()
    {
        var key = await _h.CreateAsync();

        var stored = await _h.Local.WithAsync(async s => (await s.GetRequiredService<IContext>().ReplicationConfigs!.AsNoTracking().SingleAsync(Token)).ProtectedApiKey);
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain(_h.PeerKey, stored);

        var view = await _h.Local.WithAsync(s => s.GetRequiredService<IReplicationAdministration>().GetAsync(key, Token));
        Assert.True(view!.HasApiKey);
        Assert.Null(view.Config.ProtectedApiKey);

        var json = await _h.Local.GetAsync($"/api/v1/replication/configs/{key}", AdminApiHarness.GlobalKey);
        Assert.Equal(HttpStatusCode.OK, json.Status);
        Assert.DoesNotContain(_h.PeerKey, json.Body);
        Assert.DoesNotContain(stored!, json.Body);
        Assert.True(json.Json["replication"]!["hasApiKey"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ThePeerKeyIsDroppedWhenTheAddressChangesAndKeptWhenItDoesNot()
    {
        var key = await _h.CreateAsync();
        ReplicationInput Edit(string url, string? apiKey = null)
            => new(key, "r1", "default", ReplicationDirection.Push, url, "default", apiKey, false, true, false, null, false, ReplicationConflict.TargetWins);

        async Task<bool> HasKeyAsync() => (await _h.Local.WithAsync(s => s.GetRequiredService<IReplicationAdministration>().GetAsync(key, Token)))!.HasApiKey;

        await _h.Local.WithAsync(async s => await s.GetRequiredService<IReplicationAdministration>().SaveAsync(Edit(ReplicationHarness.PeerAddress + "sub/"), "root", Token));
        Assert.True(await HasKeyAsync());

        await _h.Local.WithAsync(async s => await s.GetRequiredService<IReplicationAdministration>().SaveAsync(Edit("http://elsewhere.test/"), "root", Token));
        Assert.False(await HasKeyAsync());
    }

    [Fact]
    public async Task AnAddressThatBlockPrivateAddressesRefusesIsNotSaved()
    {
        using var guarded = new ReplicationHarness(_output, new Dictionary<string, string> { { "Remote:BlockPrivateAddresses", "true" } });

        var e = await Assert.ThrowsAsync<ReplicationException>(() => guarded.Local.WithAsync(async s => await s.GetRequiredService<IReplicationAdministration>().SaveAsync(
            new ReplicationInput(0, "x", "default", ReplicationDirection.Push, "http://localhost:5000/", "default", "k", false, true, false, null, false, ReplicationConflict.TargetWins), "root", Token)));

        Assert.Contains("non-public", e.Message);
    }

    [Fact]
    public async Task DeletingARepositoryRemovesItsReplicationsLogAndLedger()
    {
        await _h.Local.App.CreateRepositoryAsync("gone", Token);
        await AddNuGetAsync(_h.Local, "Contoso.Lib", "1.0.0", "gone");
        var key = await _h.CreateAsync("g", "gone", "default");
        await _h.RunAsync(key);

        await _h.Local.WithAsync(async s =>
        {
            using var scope = await _h.Local.App.CreateRepositoryScopeAsync("gone", Token);
            await scope.ServiceProvider.GetRequiredService<IPackageDeletionService>().TryHardDeletePackageAsync("Contoso.Lib", NuGetVersion.Parse("1.0.0"), Token);
            return 0;
        });
        await _h.Local.WithAsync(async s =>
        {
            await s.GetRequiredService<IRepositoryAdministration>().DeleteAsync("gone", Token);
            return 0;
        });

        var counts = await _h.Local.WithAsync(async s =>
        {
            var context = s.GetRequiredService<IContext>();
            return (await context.ReplicationConfigs!.CountAsync(Token), await context.ReplicationRuns!.CountAsync(Token), await context.ReplicationItems!.CountAsync(Token));
        });
        Assert.Equal((0, 0, 0), counts);
    }
}
