using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// Garbage collection of Docker blobs (P5): layers no manifest uses, expired upload sessions, blobs no image holds and files no row
/// names are found (dry run) and removed, and what a push in progress or a manifest still uses is not. Time is a
/// <see cref="ManualSystemTime"/>; images are put through the registry service, the Operations page starts the job.
/// </summary>
public class OciGarbageCollectorTests : IDisposable
{
    private const string Repo = "reg";

    private readonly ManualSystemTime _clock = new();
    private readonly FakeOciUpstream _upstream = new();
    private readonly ManualTimeProvider _time = new();
    private readonly BaGetApplication _app;
    private readonly Browser _admin;
    private int _seed;

    public OciGarbageCollectorTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Accounts:Enabled", "true" },
                { "ApiKey", "" },
            },
            configureServices: services =>
            {
                services.AddSingleton<SystemTime>(_clock);
                services.AddSingleton<IRemoteHttpClientFactory>(new ForwardingRemoteHttpClientFactory(new System.Net.Http.HttpClient(_upstream)));
                services.AddSingleton(new RemoteRepositoryState(_time));
            });
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
        _app.CreateDockerRepositoryAsync(Repo, anonymousRead: true, Token).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<T> InRepoAsync<T>(Func<IServiceProvider, Task<T>> action, string repository = Repo)
    {
        using var scope = await _app.CreateRepositoryScopeAsync(repository, Token);

        return await action(scope.ServiceProvider);
    }

    private Task<OciGcReport> RunAsync(bool dryRun, string repository = Repo)
        => InRepoAsync(sp => sp.GetRequiredService<IOciGarbageCollector>().RunAsync(dryRun, Token), repository);

    private static async Task PutBlobAsync(IServiceProvider sp, string image, byte[] content)
        => await sp.GetRequiredService<IOciRegistry>().PutBlobAsync(image, OciTestImage.Digest(content), new MemoryStream(content), Token);

    private async Task<OciTestImage> PushImageAsync(string image, string tag, string? seed = null)
    {
        var content = OciTestImage.Create(seed ?? "img" + _seed++);

        await InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, image, content.Config);
            foreach (var layer in content.Layers) await PutBlobAsync(sp, image, layer);
            await sp.GetRequiredService<IOciRegistry>().PutManifestAsync(image, tag, OciTestImage.ManifestType, content.Manifest, Token);

            return 0;
        });

        return content;
    }

    private async Task<byte[]> PushLooseLayerAsync(string image, string seed)
    {
        var bytes = Encoding.UTF8.GetBytes("loose layer " + seed + new string('x', 200));
        await InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, image, bytes);

            return 0;
        });

        return bytes;
    }

    private Task<bool> StoredAsync(string digest, string repository = Repo)
        => InRepoAsync(async sp => await sp.GetRequiredService<OciRegistryService>().FindRepositoryBlobAsync(digest, Token) != null, repository);

    private Task<bool> ReadableUnderAsync(string image, string digest)
        => InRepoAsync(async sp =>
        {
            using var blob = await sp.GetRequiredService<IOciRegistry>().OpenBlobAsync(image, digest, 0, null, Token);

            return blob != null;
        });

    // ---- what is collected ----

    [Fact]
    public async Task ALayerNoManifestUsesIsCollectedOnceThePushGracePeriodIsOver()
    {
        var loose = await PushLooseLayerAsync("team/app", "a");
        var digest = OciTestImage.Digest(loose);

        // Within the grace period a push in progress (layers first, manifest last) is never touched.
        var early = await RunAsync(dryRun: false);
        Assert.Equal(0, early.UnreferencedLayers);
        Assert.True(await StoredAsync(digest));

        _clock.Advance(TimeSpan.FromHours(2));
        var report = await RunAsync(dryRun: false);

        Assert.False(report.DryRun);
        Assert.Equal(1, report.UnreferencedLayers);
        Assert.Equal(1, report.Blobs);
        Assert.Equal(loose.Length, report.Bytes);
        Assert.Contains(report.Items, i => i.Kind == OciGcItemKind.UnreferencedLayer && i.Subject == digest && i.Image == "team/app");
        Assert.False(await StoredAsync(digest));
        Assert.Contains("Removed", report.Summary);
    }

    [Fact]
    public async Task ADryRunReportsExactlyWhatARunRemovesAndChangesNothing()
    {
        var loose = await PushLooseLayerAsync("team/app", "a");
        var digest = OciTestImage.Digest(loose);
        _clock.Advance(TimeSpan.FromHours(2));

        var dry = await RunAsync(dryRun: true);
        var rowsAfterDry = await InRepoAsync(sp => sp.GetRequiredService<IContext>().OciBlobs!.CountAsync(Token));

        Assert.True(dry.DryRun);
        Assert.Equal(1, dry.Blobs);
        Assert.Contains("Dry run", dry.Summary);
        Assert.Equal(1, rowsAfterDry);
        Assert.True(await StoredAsync(digest));

        var real = await RunAsync(dryRun: false);

        Assert.Equal(dry.UnreferencedLayers, real.UnreferencedLayers);
        Assert.Equal(dry.Blobs, real.Blobs);
        Assert.Equal(dry.Bytes, real.Bytes);
        Assert.Equal(dry.OrphanFiles, real.OrphanFiles);
        Assert.False(await StoredAsync(digest));
    }

    [Fact]
    public async Task LayersAManifestUsesAreNeverCollected()
    {
        var image = await PushImageAsync("team/app", "1.0");
        _clock.Advance(TimeSpan.FromDays(30));

        var report = await RunAsync(dryRun: false);

        Assert.Equal(0, report.Blobs);
        Assert.Equal(0, report.UnreferencedLayers);
        Assert.True(await StoredAsync(image.LayerDigests[0]));
        Assert.True(await StoredAsync(image.ConfigDigest));
        Assert.True(await ReadableUnderAsync("team/app", image.LayerDigests[0]));
    }

    [Fact]
    public async Task ALayerTwoImagesShareStaysUntilNoImageHoldsItUnused()
    {
        var shared = await PushImageAsync("team/a", "1", seed: "shared");

        // A second image with the same config and layer.
        await InRepoAsync(async sp =>
        {
            var registry = sp.GetRequiredService<IOciRegistry>();
            Assert.True(await registry.MountBlobAsync("team/b", shared.ConfigDigest, "team/a", Token));
            Assert.True(await registry.MountBlobAsync("team/b", shared.LayerDigests[0], "team/a", Token));
            await registry.PutManifestAsync("team/b", "1", OciTestImage.ManifestType, shared.Manifest, Token);

            // The first image's manifest goes: its links become loose, the second image still uses both blobs.
            await registry.DeleteManifestAsync("team/a", shared.ManifestDigest, Token);

            return 0;
        });
        _clock.Advance(TimeSpan.FromHours(2));

        var report = await RunAsync(dryRun: false);

        Assert.Equal(2, report.UnreferencedLayers);
        Assert.Equal(0, report.Blobs);
        Assert.True(await StoredAsync(shared.LayerDigests[0]));
        Assert.True(await ReadableUnderAsync("team/b", shared.LayerDigests[0]));
        Assert.False(await ReadableUnderAsync("team/a", shared.LayerDigests[0]));
    }

    [Fact]
    public async Task AnImageWithAnOpenUploadKeepsItsLooseLayersAndOnceTheSessionExpiresTheyGo()
    {
        var loose = await PushLooseLayerAsync("team/app", "slow");
        var digest = OciTestImage.Digest(loose);
        await InRepoAsync(async sp =>
        {
            await sp.GetRequiredService<IOciRegistry>().StartUploadAsync("team/app", Token);

            return 0;
        });

        // Two hours on: the layer is past the grace period but the push (a big layer takes its time) is still open.
        _clock.Advance(TimeSpan.FromHours(2));
        var during = await RunAsync(dryRun: false);
        Assert.Equal(0, during.UnreferencedLayers);
        Assert.Equal(0, during.ExpiredUploads);
        Assert.True(await StoredAsync(digest));

        _clock.Advance(TimeSpan.FromHours(24));
        var after = await RunAsync(dryRun: false);

        Assert.Equal(1, after.ExpiredUploads);
        Assert.Equal(1, after.UnreferencedLayers);
        Assert.False(await StoredAsync(digest));
        Assert.Equal(0, await InRepoAsync(sp => sp.GetRequiredService<IContext>().OciUploads!.CountAsync(Token)));
    }

    [Fact]
    public async Task AnExpiredUploadSessionIsAbortedInStorageAndItsRowDeleted()
    {
        var state = await InRepoAsync(sp => sp.GetRequiredService<IOciRegistry>().StartUploadAsync("team/app", Token));
        _clock.Advance(TimeSpan.FromHours(25));

        var dry = await RunAsync(dryRun: true);
        Assert.Equal(1, dry.ExpiredUploads);
        Assert.Contains(dry.Items, i => i.Kind == OciGcItemKind.ExpiredUpload && i.Subject == state.Reference);
        Assert.NotNull(await InRepoAsync(sp => sp.GetRequiredService<IOciRegistry>().GetUploadAsync("team/app", state.Reference, Token)));

        var real = await RunAsync(dryRun: false);

        Assert.Equal(1, real.ExpiredUploads);
        Assert.Null(await InRepoAsync(sp => sp.GetRequiredService<IOciRegistry>().GetUploadAsync("team/app", state.Reference, Token)));
    }

    [Fact]
    public async Task ABlobRowNoImageHoldsIsRemovedWithItsFileAfterTheGracePeriod()
    {
        var loose = await PushLooseLayerAsync("team/app", "orphan");
        var digest = OciTestImage.Digest(loose);

        // The links are gone (a crash between the two writes of a delete, an old bug): the row has no holder.
        await InRepoAsync(async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            context.OciBlobLinks!.RemoveRange(await context.OciBlobLinks!.ToListAsync(Token));
            await context.SaveChangesAsync(Token);

            return 0;
        });

        Assert.Equal(0, (await RunAsync(dryRun: false)).Blobs);

        _clock.Advance(TimeSpan.FromHours(2));
        var report = await RunAsync(dryRun: false);

        Assert.Equal(1, report.Blobs);
        Assert.Contains(report.Items, i => i.Kind == OciGcItemKind.OrphanBlob && i.Subject == digest);
        Assert.False(await StoredAsync(digest));
        Assert.Equal(0, (await RunAsync(dryRun: true)).OrphanFiles);
    }

    [Fact]
    public async Task AFileUnderTheBlobFolderThatNoRowNamesIsRemovedAndOthersAreLeftAlone()
    {
        var image = await PushImageAsync("team/app", "1.0");
        var bytes = Encoding.UTF8.GetBytes("a file whose row never got written");
        var digest = OciTestImage.Digest(bytes);
        var path = "repos/" + Repo + "/" + OciNames.BlobPath(digest);
        var stranger = "repos/" + Repo + "/docker/blobs/readme.txt";
        var storage = _app.Services.GetRequiredService<IStorageService>();
        await storage.PutAsync(path, new MemoryStream(bytes), "application/octet-stream", Token);
        await storage.PutAsync(stranger, new MemoryStream(new byte[] { 1 }), "text/plain", Token);

        var dry = await RunAsync(dryRun: true);
        Assert.Equal(1, dry.OrphanFiles);
        Assert.Contains(dry.Items, i => i.Kind == OciGcItemKind.OrphanFile && i.Subject == digest);
        using (var stillThere = await storage.GetAsync(path, Token)) Assert.NotNull(stillThere);

        var real = await RunAsync(dryRun: false);

        Assert.Equal(1, real.OrphanFiles);
        await Assert.ThrowsAnyAsync<Exception>(async () => { using var _ = await storage.GetAsync(path, Token); });
        using (var left = await storage.GetAsync(stranger, Token)) Assert.NotNull(left);
        Assert.True(await StoredAsync(image.LayerDigests[0]));
    }

    [Fact]
    public async Task ABlobCommittedAfterTheRowsWereLoadedIsNotTakenForAnOrphanFile()
    {
        const string racing = "racegc";
        await _app.CreateDockerRepositoryAsync(racing, anonymousRead: true, Token);
        var bytes = Encoding.UTF8.GetBytes("a layer pushed while the collection runs");
        var digest = OciTestImage.Digest(bytes);

        OciGarbageCollector.AfterRowsLoaded = async name =>
        {
            if (name != racing) return;
            await InRepoAsync(sp => { return PutBlobAsync(sp, "team/app", bytes).ContinueWith(_ => 0); }, racing);
        };
        try
        {
            var report = await RunAsync(dryRun: false, racing);

            Assert.Equal(0, report.OrphanFiles);
        }
        finally
        {
            OciGarbageCollector.AfterRowsLoaded = null;
        }

        Assert.True(await StoredAsync(digest, racing));
        using var file = await _app.Services.GetRequiredService<IStorageService>().GetAsync("repos/" + racing + "/" + OciNames.BlobPath(digest), Token);
        Assert.NotNull(file);
    }

    [Fact]
    public async Task AManifestCommittedAfterThePlanWasMadeKeepsItsLayersAndLinks()
    {
        var content = OciTestImage.Create("racing-manifest");
        await InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, "team/app", content.Config);
            foreach (var layer in content.Layers) await PutBlobAsync(sp, "team/app", layer);

            return 0;
        });
        _clock.Advance(TimeSpan.FromHours(2));

        // The layers are loose and past the grace period, so the plan lists them; then a push (which saw them with HEAD and sent none) commits its manifest.
        OciGarbageCollector.AfterRowsLoaded = async name =>
        {
            if (name != Repo) return;
            await InRepoAsync(async sp =>
            {
                await sp.GetRequiredService<IOciRegistry>().PutManifestAsync("team/app", "1.0", OciTestImage.ManifestType, content.Manifest, Token);

                return 0;
            });
        };
        OciGcReport report;
        try
        {
            report = await RunAsync(dryRun: false);
        }
        finally
        {
            OciGarbageCollector.AfterRowsLoaded = null;
        }

        Assert.Equal(0, report.UnreferencedLayers);
        Assert.Equal(0, report.Blobs);
        foreach (var layer in content.Layers)
        {
            Assert.True(await StoredAsync(OciTestImage.Digest(layer)));
            Assert.True(await ReadableUnderAsync("team/app", OciTestImage.Digest(layer)));
        }
    }

    [Fact]
    public async Task OnlyTheRepositoryItRunsOnIsTouched()
    {
        await _app.CreateDockerRepositoryAsync("other", anonymousRead: true, Token);
        var loose = await PushLooseLayerAsync("team/app", "mine");
        var theirs = Encoding.UTF8.GetBytes("someone else's loose layer " + new string('y', 100));
        await InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, "team/app", theirs);

            return 0;
        }, "other");
        _clock.Advance(TimeSpan.FromHours(2));

        var report = await RunAsync(dryRun: false);

        Assert.Equal(1, report.Blobs);
        Assert.False(await StoredAsync(OciTestImage.Digest(loose)));
        Assert.True(await StoredAsync(OciTestImage.Digest(theirs), "other"));
    }

    [Fact]
    public async Task ACachedImageKeepsItsLayersAndAPurgedOnesAreCollected()
    {
        _upstream.AddImage("team/app", "1.0", "a");
        _upstream.AddImage("team/other", "1.0", "b");
        var settings = new RemoteRepositorySettings { Url = "https://" + FakeOciUpstream.RegistryHost, MetadataTtlSeconds = 600 };
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync("hub", RepositoryFormat.Docker, RepositoryKind.Remote, settings, anonymousRead: true, Token);
        }

        // Pull both images through the repository, layers included.
        var layers = new List<string>();
        foreach (var image in new[] { "team/app", "team/other" })
        {
            await InRepoAsync(async sp =>
            {
                var registry = sp.GetRequiredService<IOciRegistry>();
                Assert.NotNull(await registry.GetManifestAsync(image, "1.0", Token));
                var layer = _upstream.LayersOf(image, "1.0")[0];
                layers.Add(layer);
                using var blob = await registry.OpenBlobAsync(image, layer, 0, null, Token);
                var buffer = new MemoryStream();
                await blob!.Content.CopyToAsync(buffer, Token);

                return 0;
            }, "hub");
        }

        // The second image's manifest is dropped (an administrator purged one tag): its layer is loose from then on.
        await InRepoAsync(async sp => await sp.GetRequiredService<OciRegistryService>().DeleteReferenceAsync("team/other", "1.0", Token), "hub");
        _clock.Advance(TimeSpan.FromHours(2));

        var report = await RunAsync(dryRun: false, "hub");

        Assert.Equal(1, report.Blobs);
        Assert.True(await StoredAsync(layers[0], "hub"));
        Assert.False(await StoredAsync(layers[1], "hub"));
    }

    [Fact]
    public async Task AVirtualRepositoryHasNoBlobsToCollect()
    {
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
                .CreateAsync("all", new VirtualRepositorySettings { Members = { Repo } }, Token, null, true, RepositoryFormat.Docker);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(dryRun: true, "all"));
    }

    // ---- the Operations page ----

    private Task<HttpResponseMessage> StartAsync(string operation, string repository, string? password = Password)
        => _admin.PostAsync("admin/operations", new Dictionary<string, string> { { "operation", operation }, { "repository", repository }, { "password", password ?? string.Empty } });

    [Fact]
    public async Task TheOperationsPageOffersBothCollectionsForDockerRepositoriesOnly()
    {
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync("nuget", RepositoryFormat.NuGet, RepositoryKind.Local, Token);
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
                .CreateAsync("all", new VirtualRepositorySettings { Members = { Repo } }, Token, null, true, RepositoryFormat.Docker);
        }

        var html = await _admin.GetPageAsync("admin/operations");

        Assert.Contains($"data-operation=\"{OciGarbageCollectionOperation.DryRunName}\" data-repository=\"{Repo}\"", html);
        Assert.Contains($"data-operation=\"{OciGarbageCollectionOperation.RunName}\" data-repository=\"{Repo}\"", html);
        Assert.DoesNotContain($"data-operation=\"{OciGarbageCollectionOperation.RunName}\" data-repository=\"nuget\"", html);
        Assert.DoesNotContain($"data-operation=\"{OciGarbageCollectionOperation.RunName}\" data-repository=\"default\"", html);
        Assert.DoesNotContain($"data-operation=\"{OciGarbageCollectionOperation.RunName}\" data-repository=\"all\"", html);
        Assert.Contains("Docker garbage collection: dry run", html);
    }

    [Fact]
    public async Task ADryRunFromThePageReportsAndAFullRunRemoves()
    {
        var loose = await PushLooseLayerAsync("team/app", "ui");
        var digest = OciTestImage.Digest(loose);
        _clock.Advance(TimeSpan.FromHours(2));
        var jobs = _app.Services.GetRequiredService<AdminJobService>();

        using var dry = await StartAsync(OciGarbageCollectionOperation.DryRunName, Repo);
        Assert.Equal(HttpStatusCode.Redirect, dry.StatusCode);
        await jobs.WaitAsync(OciGarbageCollectionOperation.DryRunName, Repo);
        var dryStatus = jobs.Get(OciGarbageCollectionOperation.DryRunName, Repo)!;
        Assert.Equal(AdminJobState.Succeeded, dryStatus.State);
        Assert.Contains("Dry run, nothing removed", dryStatus.Message);
        Assert.Contains("1 blob(s)", dryStatus.Message);
        Assert.Contains(digest, dryStatus.Message);
        Assert.True(await StoredAsync(digest));

        using var run = await StartAsync(OciGarbageCollectionOperation.RunName, Repo);
        Assert.Equal(HttpStatusCode.Redirect, run.StatusCode);
        await jobs.WaitAsync(OciGarbageCollectionOperation.RunName, Repo);
        Assert.Equal(AdminJobState.Succeeded, jobs.Get(OciGarbageCollectionOperation.RunName, Repo)!.State);
        Assert.False(await StoredAsync(digest));

        var html = await _admin.GetPageAsync("admin/operations");
        Assert.Contains("Removed: 0 expired upload session(s), 1 unreferenced layer link(s), 1 blob(s)", html);
    }

    [Fact]
    public async Task TheCollectionThatRemovesAsksForThePasswordAndTheDryRunDoesNot()
    {
        var loose = await PushLooseLayerAsync("team/app", "pw");
        var digest = OciTestImage.Digest(loose);
        _clock.Advance(TimeSpan.FromHours(2));
        var jobs = _app.Services.GetRequiredService<AdminJobService>();

        var page = await _admin.GetPageAsync("admin/operations");
        Assert.Matches(@$"data-operation=""{OciGarbageCollectionOperation.RunName}"" data-repository=""{Repo}""[\s\S]*?name=""password""", page);

        using (var wrong = await StartAsync(OciGarbageCollectionOperation.RunName, Repo, password: "wrong"))
        {
            Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        }

        Assert.Null(jobs.Get(OciGarbageCollectionOperation.RunName, Repo));
        Assert.Contains("confirmation is incorrect", await _admin.GetPageAsync("admin/operations"));
        Assert.True(await StoredAsync(digest));

        using (var dry = await StartAsync(OciGarbageCollectionOperation.DryRunName, Repo, password: null))
        {
            Assert.Equal(HttpStatusCode.Redirect, dry.StatusCode);
        }

        await jobs.WaitAsync(OciGarbageCollectionOperation.DryRunName, Repo);
        Assert.Equal(AdminJobState.Succeeded, jobs.Get(OciGarbageCollectionOperation.DryRunName, Repo)!.State);
    }

    [Fact]
    public async Task ACollectionIsRefusedForARepositoryItDoesNotApplyTo()
    {
        using var response = await StartAsync(OciGarbageCollectionOperation.RunName, "default");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(_app.Services.GetRequiredService<AdminJobService>().Get(OciGarbageCollectionOperation.RunName, "default"));
    }

    [Fact]
    public async Task ARealRunIsRefusedWhileTheServerIsReadOnlyButADryRunReports()
    {
        await PushLooseLayerAsync("team/app", "gate");
        _clock.Advance(TimeSpan.FromHours(2));
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            await Assert.ThrowsAsync<MaintenanceException>(() => RunAsync(dryRun: false));
            Assert.Equal(1, (await RunAsync(dryRun: true)).Blobs);
        }
        finally
        {
            gate.Open();
        }
    }

    [Fact]
    public async Task ARunIsAuditedWithItsOutcome()
    {
        var jobs = _app.Services.GetRequiredService<AdminJobService>();
        using var run = await StartAsync(OciGarbageCollectionOperation.RunName, Repo);
        await jobs.WaitAsync(OciGarbageCollectionOperation.RunName, Repo);

        var status = jobs.Get(OciGarbageCollectionOperation.RunName, Repo)!;
        Assert.Equal("root", status.Actor);
        Assert.Contains("Removed: 0 expired", status.Message);
    }

    // ---- layer usage counts, prune, foreign layers ----

    private static (byte[] Manifest, byte[] Config) ManifestOver(string layerDigest, long layerSize, string seed, string? layerType = null, string? extraLayerFields = null)
    {
        var config = Encoding.UTF8.GetBytes($"{{\"architecture\":\"amd64\",\"os\":\"linux\",\"seed\":\"{seed}\"}}");
        var json = "{\"schemaVersion\":2,\"mediaType\":\"" + OciTestImage.ManifestType + "\","
            + $"\"config\":{{\"mediaType\":\"{OciTestImage.ConfigType}\",\"digest\":\"{OciTestImage.Digest(config)}\",\"size\":{config.Length}}},"
            + $"\"layers\":[{{\"mediaType\":\"{layerType ?? OciTestImage.LayerType}\",\"digest\":\"{layerDigest}\",\"size\":{layerSize}{extraLayerFields}}}]}}";

        return (Encoding.UTF8.GetBytes(json), config);
    }

    private Task<int[]> ManifestCountsAsync()
        => InRepoAsync(async sp => await sp.GetRequiredService<IContext>().OciBlobLinks!.AsNoTracking().Select(l => l.Manifests).ToArrayAsync(Token));

    [Fact]
    public async Task AMountOfABlobWhoseFileIsGoneIsRefusedAndRecordsNothing()
    {
        var layer = Encoding.UTF8.GetBytes("mounted layer " + new string('m', 100));
        var digest = OciTestImage.Digest(layer);
        await InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, "team/source", layer);
            await sp.GetRequiredService<IStorageService>().DeleteAsync(RepositoryStorage.Scope(sp.GetRequiredService<IRepositoryContext>().Current, OciNames.BlobPath(digest)), Token);

            return 0;
        });

        var mounted = await InRepoAsync(sp => sp.GetRequiredService<IOciRegistry>().MountBlobAsync("team/target", digest, "team/source", Token));

        Assert.False(mounted);
        Assert.Null(await InRepoAsync(sp => sp.GetRequiredService<IOciRegistry>().GetBlobInfoAsync("team/target", digest, Token)));
    }

    [Fact]
    public async Task ASlowMonolithicPutDoesNotHoldTheDigestLockWhileItsBodyIsRead()
    {
        var layer = Encoding.UTF8.GetBytes("slow layer " + new string('w', 100));
        var digest = OciTestImage.Digest(layer);
        var body = new GatedStream(layer);
        var slow = InRepoAsync(async sp => await sp.GetRequiredService<IOciRegistry>().PutBlobAsync("team/slow", digest, body, Token));
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        // The same digest is pushed again and finishes while the first body is still being read.
        await InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, "team/other", layer);

            return 0;
        }).WaitAsync(TimeSpan.FromSeconds(10), Token);

        body.Release();
        await slow.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.NotNull(await InRepoAsync(sp => sp.GetRequiredService<IOciRegistry>().GetBlobInfoAsync("team/slow", digest, Token)));
    }

    private sealed class GatedStream : MemoryStream
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedStream(byte[] content) : base(content)
        {
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    [Fact]
    public async Task ManifestsPushedAtTheSameTimeEachCountAsAUserOfTheirSharedLayer()
    {
        var layer = Encoding.UTF8.GetBytes("shared layer " + new string('s', 100));
        var layerDigest = OciTestImage.Digest(layer);
        await InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, "team/app", layer);

            return 0;
        });

        var manifests = Enumerable.Range(0, 6).Select(i => ManifestOver(layerDigest, layer.Length, "m" + i)).ToList();
        await Task.WhenAll(manifests.Select((m, i) => InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, "team/app", m.Config);
            await sp.GetRequiredService<IOciRegistry>().PutManifestAsync("team/app", "t" + i, OciTestImage.ManifestType, m.Manifest, Token);

            return 0;
        })));

        // Every manifest uses the shared layer; each of the 6 configs is used by one.
        var counts = await ManifestCountsAsync();
        Assert.Equal(6, counts.Max());
        Assert.Equal(6, counts.Count(c => c == 1));

        // Giving every reference back leaves nothing used.
        foreach (var m in manifests)
        {
            await InRepoAsync(async sp => await sp.GetRequiredService<IOciRegistry>().DeleteManifestAsync("team/app", OciTestImage.Digest(m.Manifest), Token));
        }

        Assert.All(await ManifestCountsAsync(), c => Assert.Equal(0, c));
    }

    [Fact]
    public async Task PruneLeavesTheLooseLayersOfAnImageWithAPushInProgress()
    {
        var loose = await PushLooseLayerAsync("team/app", "pruned");
        await InRepoAsync(async sp => (await sp.GetRequiredService<IOciRegistry>().StartUploadAsync("team/app", Token)).Reference);
        _clock.Advance(TimeSpan.FromHours(2));

        var result = await InRepoAsync(sp => sp.GetRequiredService<OciRegistryService>().PruneAsync("team/app", Token));

        Assert.Equal(0, result.Blobs);
        Assert.True(await StoredAsync(OciTestImage.Digest(loose)));
    }

    [Fact]
    public async Task PruneRemovesTheLooseLayersOfAnImageWithNoPushInProgress()
    {
        var loose = await PushLooseLayerAsync("team/app", "pruned2");
        _clock.Advance(TimeSpan.FromHours(2));

        var result = await InRepoAsync(sp => sp.GetRequiredService<OciRegistryService>().PruneAsync("team/app", Token));

        Assert.Equal(1, result.Blobs);
        Assert.False(await StoredAsync(OciTestImage.Digest(loose)));
    }

    [Fact]
    public async Task ALayerAManifestUsesCannotBeDeletedAsABlob()
    {
        var content = await PushImageAsync("team/app", "1.0");

        var exception = await Assert.ThrowsAsync<OciException>(() => InRepoAsync(async sp =>
            await sp.GetRequiredService<IOciRegistry>().DeleteBlobAsync("team/app", content.LayerDigests[0], Token)));

        Assert.Equal(OciErrorCode.Unsupported, exception.Code);
        Assert.True(await StoredAsync(content.LayerDigests[0]));
    }

    [Fact]
    public async Task AManifestWithAForeignLayerIsAcceptedWithoutThatLayerBeingStored()
    {
        var foreignDigest = OciTestImage.Digest(Encoding.UTF8.GetBytes("a windows base layer"));
        var (manifest, config) = ManifestOver(foreignDigest, 4096, "foreign", "application/vnd.docker.image.rootfs.foreign.diff.tar.gzip", ",\"urls\":[\"https://mcr.example/layer\"]");

        await InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, "team/app", config);
            await sp.GetRequiredService<IOciRegistry>().PutManifestAsync("team/app", "win", OciTestImage.ManifestType, manifest, Token);

            return 0;
        });

        // Only the config is stored and counted.
        Assert.Equal(new[] { 1 }, await ManifestCountsAsync());
        Assert.True(await InRepoAsync(async sp => await sp.GetRequiredService<IOciRegistry>().DeleteManifestAsync("team/app", OciTestImage.Digest(manifest), Token)));
    }

    [Fact]
    public async Task ALayerWithoutUrlsIsStillRequiredEvenWithAForeignMediaType()
    {
        var digest = OciTestImage.Digest(Encoding.UTF8.GetBytes("missing"));
        var (manifest, config) = ManifestOver(digest, 10, "needs", "application/vnd.docker.image.rootfs.foreign.diff.tar.gzip");

        var exception = await Assert.ThrowsAsync<OciException>(() => InRepoAsync(async sp =>
        {
            await PutBlobAsync(sp, "team/app", config);
            await sp.GetRequiredService<IOciRegistry>().PutManifestAsync("team/app", "x", OciTestImage.ManifestType, manifest, Token);

            return 0;
        }));

        Assert.Equal(OciErrorCode.ManifestBlobUnknown, exception.Code);
    }
}
