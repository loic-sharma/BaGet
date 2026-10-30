using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
/// Retention and cleanup (P6) over the real services: the per-format hooks (NuGet, npm, Maven, Docker) list and delete what a
/// policy selects, the dry run is exactly what the run deletes, a legal hold keeps what a rule selected, downloads are recorded
/// for the "unused" rule, the worker starts due schedules, the maintenance gate refuses a deleting run. Time is a
/// <see cref="ManualSystemTime"/>, so versions are published on chosen days.
/// </summary>
public class RetentionTests : IDisposable
{
    private readonly ManualSystemTime _clock = new();
    private readonly RecordingAudit _audit = new();
    private readonly BaGetApplication _app;
    private int _seed;

    // Set by a test: the maintenance gate closes after this many successful deletions (0 = never).
    private int CloseGateAfterDeletions;

    public RetentionTests(ITestOutputHelper output)
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
                services.AddSingleton<IAdminAudit>(_audit);
                services.AddTransient<IPackageDeletionService>(sp => new GateClosingDeletion(
                    ActivatorUtilities.CreateInstance<PackageDeletionService>(sp),
                    sp.GetRequiredService<MaintenanceGate>(),
                    () => CloseGateAfterDeletions));
            });
    }

    public void Dispose() => _app.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly BaGetPrincipal Admin = new(null, "root", isSuperuser: true, ApiKeyScopes.All, "*", isAdministrator: true);

    private async Task<T> InRepoAsync<T>(string? repository, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = await _app.CreateRepositoryScopeAsync(repository, Token);

        return await action(scope.ServiceProvider);
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _app.Services.CreateScope();

        return await action(scope.ServiceProvider);
    }

    private static RetentionPolicyInput Input(
        string name,
        string? repository = "default",
        string? project = null,
        int? keep = null,
        int? olderThan = null,
        int? unused = null,
        bool prerelease = false,
        long? cache = null,
        int? untagged = null,
        bool gc = false,
        int? schedule = null,
        bool enabled = true,
        string? patterns = null)
        => new(0, name, repository, project, enabled, patterns, keep, olderThan, unused, prerelease, cache, untagged, gc, schedule);

    private Task<RetentionPolicy> SaveAsync(RetentionPolicyInput input)
        => InScopeAsync(sp => sp.GetRequiredService<IRetentionAdministration>().SavePolicyAsync(input, "tester", Token));

    private Task<RetentionReport> RunAsync(RetentionPolicy policy, bool dryRun)
        => InScopeAsync(sp => sp.GetRequiredService<RetentionEngine>().RunAsync(policy.Key, dryRun, "tester", Token));

    private Task<IReadOnlyList<FormatVersion>> ListAsync(string repository, RepositoryFormat format)
        => InRepoAsync(repository == "default" ? null : repository, sp => sp.GetFormatService<IFormatRetentionHooks>(format)!.ListVersionsAsync(Token));

    private async Task<string[]> StoredAsync(string repository, RepositoryFormat format)
        => (await ListAsync(repository, format)).Select(v => v.Name + " " + v.Version).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    private static string[] Items(RetentionReport report, RetentionOutcome outcome)
        => report.Repositories.SelectMany(r => r.Items).Where(i => i.Outcome == outcome).Select(i => i.Name + " " + i.Version).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    private async Task PushNuGetAsync(string id, string version, string? repository = null)
    {
        using var package = TestPackages.Build(id, version);
        await _app.AddPackageAsync(package, Token, repository);
    }

    private async Task HoldAsync(string repository, string name, string? version = null)
        => await InScopeAsync(async sp => await sp.GetRequiredService<IRetentionAdministration>().AddHoldAsync(repository, name, version, "case 42", "tester", Token));

    private async Task CreateAsync(string name, RepositoryFormat format)
        => await InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IRepositoryService>().CreateAsync(name, format, RepositoryKind.Local, new Dictionary<string, string?>(), anonymousRead: true, Token);

            return 0;
        });

    // ---- NuGet ----

    [Fact]
    public async Task NuGetKeepLastDeletesTheOlderVersionsAndTheDryRunIsExactlyWhatTheRunDeletes()
    {
        await PushNuGetAsync("Retain.Me", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        await PushNuGetAsync("Retain.Me", "2.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        await PushNuGetAsync("Retain.Me", "3.0.0");
        _clock.Advance(TimeSpan.FromDays(1));
        var policy = await SaveAsync(Input("keep-one", keep: 1));

        var dry = await RunAsync(policy, dryRun: true);

        Assert.True(dry.DryRun);
        Assert.Equal(new[] { "Retain.Me 1.0.0", "Retain.Me 2.0.0" }, Items(dry, RetentionOutcome.Planned));
        Assert.Equal(3, (await StoredAsync("default", RepositoryFormat.NuGet)).Length);

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(Items(dry, RetentionOutcome.Planned), Items(run, RetentionOutcome.Deleted));
        Assert.Equal(2, run.Deleted);
        Assert.Equal(new[] { "Retain.Me 3.0.0" }, await StoredAsync("default", RepositoryFormat.NuGet));

        // The files are gone with the rows.
        await InRepoAsync(null, async sp =>
        {
            var storage = sp.GetRequiredService<IPackageStorageService>();
            Assert.Null(await TryAsync(() => storage.GetPackageStreamAsync("Retain.Me", NuGetVersion.Parse("1.0.0"), Token)));
            Assert.NotNull(await storage.GetPackageStreamAsync("Retain.Me", NuGetVersion.Parse("3.0.0"), Token));

            return 0;
        });
    }

    private static async Task<Stream?> TryAsync(Func<Task<Stream?>> action)
    {
        try
        {
            return await action();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    [Fact]
    public async Task ARunAfterARunFindsNothingMoreAndRecordsItselfOnThePolicy()
    {
        await PushNuGetAsync("Twice.Run", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(5));
        await PushNuGetAsync("Twice.Run", "2.0.0");
        var policy = await SaveAsync(Input("twice", keep: 1));

        var first = await RunAsync(policy, dryRun: false);
        var second = await RunAsync(policy, dryRun: false);

        Assert.Equal(1, first.Deleted);
        Assert.Equal(0, second.Deleted);
        var stored = await InScopeAsync(sp => sp.GetRequiredService<IRetentionAdministration>().GetPolicyAsync(policy.Key, Token));
        Assert.NotNull(stored!.Policy.LastRun);
        Assert.Contains("Deleted 0 version(s)", stored.Policy.LastResult);
    }

    [Fact]
    public async Task OlderThanAndPrereleaseOnlyLookAtPrereleaseVersionsOnly()
    {
        await PushNuGetAsync("Pre.Lib", "1.0.0");
        await PushNuGetAsync("Pre.Lib", "1.1.0-beta");
        await PushNuGetAsync("Pre.Lib", "1.2.0-beta");
        _clock.Advance(TimeSpan.FromDays(40));
        await PushNuGetAsync("Pre.Lib", "1.3.0-beta");
        var policy = await SaveAsync(Input("old-betas", olderThan: 30, prerelease: true));

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "Pre.Lib 1.1.0-beta", "Pre.Lib 1.2.0-beta" }, Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "Pre.Lib 1.0.0", "Pre.Lib 1.3.0-beta" }, await StoredAsync("default", RepositoryFormat.NuGet));
    }

    [Fact]
    public async Task UnusedVersionsAreFoundByTheirRecordedDownloads()
    {
        await PushNuGetAsync("Used.Lib", "1.0.0");
        await PushNuGetAsync("Used.Lib", "2.0.0");

        // The policy watches from its creation: a version nobody is recorded to download counts as unused only N days after that.
        var policy = await SaveAsync(Input("unused", unused: 30));
        _clock.Advance(TimeSpan.FromDays(40));

        // Version 2 is downloaded today, over HTTP like a client would.
        using (var client = _app.CreateClient())
        using (var response = await client.GetAsync("v3/package/used.lib/2.0.0/used.lib.2.0.0.nupkg", Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var recorder = _app.Services.GetRequiredService<ArtifactUsageRecorder>();
        Assert.True(recorder.Pending > 0);
        Assert.True(await recorder.FlushAsync(_app.Services.GetRequiredService<IServiceScopeFactory>(), Token) > 0);
        Assert.Equal(0, recorder.Pending);

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "Used.Lib 1.0.0" }, Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "Used.Lib 2.0.0" }, await StoredAsync("default", RepositoryFormat.NuGet));
    }

    [Fact]
    public async Task EnablingTheUnusedRuleOnAnOldPolicyWatchesFromThatMoment()
    {
        await PushNuGetAsync("Old.Lib", "1.0.0");
        await PushNuGetAsync("Old.Lib", "2.0.0");

        // A keep-last policy a year old, then edited to add the unused rule: nothing was watched for downloads until now.
        var created = await SaveAsync(Input("grows", keep: 5));
        _clock.Advance(TimeSpan.FromDays(365));
        var policy = await SaveAsync(Input("grows", unused: 30) with { Key = created.Key });

        var early = await RunAsync(policy, dryRun: false);
        Assert.Empty(Items(early, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "Old.Lib 1.0.0", "Old.Lib 2.0.0" }, await StoredAsync("default", RepositoryFormat.NuGet));

        _clock.Advance(TimeSpan.FromDays(31));
        var later = await RunAsync(policy, dryRun: true);
        Assert.Equal(new[] { "Old.Lib 1.0.0", "Old.Lib 2.0.0" }, Items(later, RetentionOutcome.Planned));
    }

    [Fact]
    public async Task ALegalHoldOnAVersionOrAWholePackageKeepsWhatARuleSelected()
    {
        await PushNuGetAsync("Held.One", "1.0.0");
        await PushNuGetAsync("Held.One", "2.0.0");
        await PushNuGetAsync("Held.Two", "1.0.0");
        await PushNuGetAsync("Free.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(100));
        await HoldAsync("default", "held.one", "1.0");
        await HoldAsync("default", "Held.Two");
        var policy = await SaveAsync(Input("everything-old", olderThan: 1));

        var dry = await RunAsync(policy, dryRun: true);
        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "Held.One 1.0.0", "Held.Two 1.0.0" }, Items(dry, RetentionOutcome.Held));
        Assert.Equal(new[] { "Free.Lib 1.0.0", "Held.One 2.0.0" }, Items(dry, RetentionOutcome.Planned));
        Assert.Equal(Items(dry, RetentionOutcome.Planned), Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "Held.One 1.0.0", "Held.Two 1.0.0" }, await StoredAsync("default", RepositoryFormat.NuGet));
    }

    [Fact]
    public async Task AHoldPlacedAfterTheDryRunIsHonouredByTheRun()
    {
        await PushNuGetAsync("Late.Hold", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        var policy = await SaveAsync(Input("late", olderThan: 1));

        var dry = await RunAsync(policy, dryRun: true);
        await HoldAsync("default", "Late.Hold", "1.0.0");
        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "Late.Hold 1.0.0" }, Items(dry, RetentionOutcome.Planned));
        Assert.Empty(Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "Late.Hold 1.0.0" }, await StoredAsync("default", RepositoryFormat.NuGet));
    }

    // ---- npm ----

    private async Task PublishNpmAsync(string repository, string name, string version)
    {
        var tarball = NpmTestPackages.Tarball(name, version);
        var result = await InRepoAsync(repository, sp => sp.GetRequiredService<INpmRegistry>().PublishTarballAsync(Admin, new MemoryStream(tarball.Bytes), "latest", Token));
        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task NpmVersionsAreDeletedWithTheirTarballsAndLatestMovesToWhatIsLeft()
    {
        await CreateAsync("web", RepositoryFormat.Npm);
        await PublishNpmAsync("web", "left-pad", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        await PublishNpmAsync("web", "left-pad", "1.1.0");
        _clock.Advance(TimeSpan.FromDays(10));
        await PublishNpmAsync("web", "left-pad", "1.2.0-rc.1");
        await PublishNpmAsync("web", "@acme/tool", "1.0.0");
        var policy = await SaveAsync(Input("npm-keep", "web", keep: 1));

        var dry = await RunAsync(policy, dryRun: true);
        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "left-pad 1.0.0", "left-pad 1.1.0" }, Items(dry, RetentionOutcome.Planned));
        Assert.Equal(Items(dry, RetentionOutcome.Planned), Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "@acme/tool 1.0.0", "left-pad 1.2.0-rc.1" }, await StoredAsync("web", RepositoryFormat.Npm));
        await InRepoAsync("web", async sp =>
        {
            var tags = await sp.GetRequiredService<INpmRegistry>().GetDistTagsAsync("left-pad", Token);
            Assert.Equal("1.2.0-rc.1", tags!["latest"]);
            Assert.Null(await sp.GetRequiredService<INpmRegistry>().OpenTarballAsync("left-pad", "left-pad-1.0.0.tgz", Token));

            return 0;
        });
    }

    [Fact]
    public async Task AnNpmPackageGoesWhenItsLastVersionDoes()
    {
        await CreateAsync("web", RepositoryFormat.Npm);
        await PublishNpmAsync("web", "gone", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(100));
        var policy = await SaveAsync(Input("npm-age", "web", olderThan: 30));

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(1, run.Deleted);
        Assert.Empty(await StoredAsync("web", RepositoryFormat.Npm));
        await InRepoAsync("web", async sp =>
        {
            Assert.Null(await sp.GetRequiredService<INpmRegistry>().GetPackageAsync("gone", Token));

            return 0;
        });
    }

    [Fact]
    public async Task AnNpmHoldNameIsKeptInTheFormatsOwnForm()
    {
        await CreateAsync("web", RepositoryFormat.Npm);
        await PublishNpmAsync("web", "@acme/tool", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(100));
        await HoldAsync("web", "@ACME/Tool", "1.0.0");
        var policy = await SaveAsync(Input("npm-held", "web", olderThan: 1));

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "@acme/tool 1.0.0" }, Items(run, RetentionOutcome.Held));
        Assert.Equal(0, run.Deleted);
        var holds = await InScopeAsync(sp => sp.GetRequiredService<IRetentionAdministration>().ListHoldsAsync("web", Token));
        Assert.Equal("@acme/tool", Assert.Single(holds).Name);
    }

    // ---- Maven ----

    private async Task DeployMavenAsync(string repository, string group, string artifact, string version)
    {
        using var file = new MemoryStream(Encoding.UTF8.GetBytes($"{artifact} {version}"));
        var result = await InRepoAsync(repository, sp => sp.GetRequiredService<IMavenRepository>().DeployAsync(Admin, new MavenDeployment { GroupId = group, ArtifactId = artifact, Version = version, File = file }, Token));
        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task MavenSnapshotsAreTheOnlyCandidatesWhenThePolicyIsPrereleaseOnly()
    {
        await _app.CreateMavenRepositoryAsync("jars", policy: "Both", cancellationToken: Token);
        await DeployMavenAsync("jars", "org.acme", "lib", "1.0");
        await DeployMavenAsync("jars", "org.acme", "lib", "1.1-SNAPSHOT");
        await DeployMavenAsync("jars", "org.acme", "lib", "1.2-SNAPSHOT");
        _clock.Advance(TimeSpan.FromDays(60));
        await DeployMavenAsync("jars", "org.acme", "lib", "1.3-SNAPSHOT");
        var policy = await SaveAsync(Input("snapshots", "jars", olderThan: 30, prerelease: true));

        var dry = await RunAsync(policy, dryRun: true);
        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "org.acme:lib 1.1-SNAPSHOT", "org.acme:lib 1.2-SNAPSHOT" }, Items(dry, RetentionOutcome.Planned));
        Assert.Equal(Items(dry, RetentionOutcome.Planned), Items(run, RetentionOutcome.Deleted));
        Assert.True(dry.Repositories.Single().Items.All(i => i.Size > 0));
        Assert.Equal(new[] { "org.acme:lib 1.0", "org.acme:lib 1.3-SNAPSHOT" }, await StoredAsync("jars", RepositoryFormat.Maven));
    }

    [Fact]
    public async Task AMavenKeepLastRemovesTheFilesOfTheOlderVersions()
    {
        await CreateAsync("jars", RepositoryFormat.Maven);
        await DeployMavenAsync("jars", "org.acme", "lib", "1.0");
        _clock.Advance(TimeSpan.FromDays(1));
        await DeployMavenAsync("jars", "org.acme", "lib", "2.0");
        var policy = await SaveAsync(Input("maven-keep", "jars", keep: 1));

        await RunAsync(policy, dryRun: false);

        await InRepoAsync("jars", async sp =>
        {
            var maven = sp.GetRequiredService<IMavenRepository>();
            var coordinates = MavenNames.ParsePath("org/acme/lib/1.0/lib-1.0.jar")!;
            Assert.Null(await maven.OpenAsync(coordinates, Token));
            var kept = await maven.OpenAsync(MavenNames.ParsePath("org/acme/lib/2.0/lib-2.0.jar")!, Token);
            Assert.NotNull(kept);
            kept!.Content.Dispose();

            return 0;
        });
    }

    // ---- Docker ----

    private async Task<OciTestImage> PushImageAsync(string repository, string image, string tag, string? seed = null)
    {
        var content = OciTestImage.Create(seed ?? "img" + _seed++);

        await InRepoAsync(repository, async sp =>
        {
            var registry = sp.GetRequiredService<IOciRegistry>();
            await registry.PutBlobAsync(image, OciTestImage.Digest(content.Config), new MemoryStream(content.Config), Token);
            foreach (var layer in content.Layers) await registry.PutBlobAsync(image, OciTestImage.Digest(layer), new MemoryStream(layer), Token);
            await registry.PutManifestAsync(image, tag, OciTestImage.ManifestType, content.Manifest, Token);

            return 0;
        });

        return content;
    }

    private Task<IReadOnlyList<string>> TagsAsync(string repository, string image)
        => InRepoAsync(repository, async sp => (await sp.GetRequiredService<IOciRegistry>().ListTagsAsync(image, null, null, Token))?.Tags ?? Array.Empty<string>());

    private Task<bool> ManifestExistsAsync(string repository, string image, string digest)
        => InRepoAsync(repository, async sp => await sp.GetRequiredService<IOciRegistry>().GetManifestAsync(image, digest, Token) != null);

    [Fact]
    public async Task DockerTagsAreVersionsAndDeletingOneLeavesItsManifestUntaggedUntilTheUntaggedRuleTakesIt()
    {
        await CreateAsync("reg", RepositoryFormat.Docker);
        var old = await PushImageAsync("reg", "team/app", "v1");
        _clock.Advance(TimeSpan.FromDays(10));
        var mid = await PushImageAsync("reg", "team/app", "v2");
        _clock.Advance(TimeSpan.FromDays(10));
        var current = await PushImageAsync("reg", "team/app", "v3");
        var keep = await SaveAsync(Input("docker-keep", "reg", keep: 1));

        var dry = await RunAsync(keep, dryRun: true);
        var run = await RunAsync(keep, dryRun: false);

        Assert.Equal(new[] { "team/app v1", "team/app v2" }, Items(dry, RetentionOutcome.Planned));
        Assert.Equal(Items(dry, RetentionOutcome.Planned), Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "v3" }, await TagsAsync("reg", "team/app"));

        // The manifests are still there, untagged.
        Assert.True(await ManifestExistsAsync("reg", "team/app", old.ManifestDigest));
        Assert.True(await ManifestExistsAsync("reg", "team/app", mid.ManifestDigest));

        // Not old enough yet.
        var untagged = await SaveAsync(Input("docker-untagged", "reg", untagged: 7, gc: true));
        var tooSoon = await SaveAsync(Input("docker-untagged-late", "reg", untagged: 100));
        Assert.Equal(0, (await RunAsync(tooSoon, dryRun: false)).Deleted);

        _clock.Advance(TimeSpan.FromDays(30));
        var dryUntagged = await RunAsync(untagged, dryRun: true);
        var runUntagged = await RunAsync(untagged, dryRun: false);

        Assert.Equal(new[] { "team/app " + old.ManifestDigest, "team/app " + mid.ManifestDigest }.OrderBy(s => s, StringComparer.Ordinal), Items(dryUntagged, RetentionOutcome.Planned));
        Assert.Equal(Items(dryUntagged, RetentionOutcome.Planned), Items(runUntagged, RetentionOutcome.Deleted));
        Assert.False(await ManifestExistsAsync("reg", "team/app", old.ManifestDigest));
        Assert.True(await ManifestExistsAsync("reg", "team/app", current.ManifestDigest));

        // The garbage collection that followed freed the layers of the deleted images, not the tagged one's.
        var gc = Assert.Single(runUntagged.Repositories).GarbageCollection;
        Assert.NotNull(gc);
        Assert.False(gc!.DryRun);
        Assert.True(await InRepoAsync("reg", async sp => await sp.GetRequiredService<OciRegistryService>().FindRepositoryBlobAsync(old.LayerDigests[0], Token) == null));
        Assert.True(await InRepoAsync("reg", async sp => await sp.GetRequiredService<OciRegistryService>().FindRepositoryBlobAsync(current.LayerDigests[0], Token) != null));
    }

    [Fact]
    public async Task AnIndexKeepsItsChildManifestsAndAReferrerKeepsItsSubjectRelationship()
    {
        await CreateAsync("reg", RepositoryFormat.Docker);
        var child = OciTestImage.Create("child");
        await InRepoAsync("reg", async sp =>
        {
            var registry = sp.GetRequiredService<IOciRegistry>();
            await registry.PutBlobAsync("team/app", OciTestImage.Digest(child.Config), new MemoryStream(child.Config), Token);
            foreach (var layer in child.Layers) await registry.PutBlobAsync("team/app", OciTestImage.Digest(layer), new MemoryStream(layer), Token);
            await registry.PutManifestAsync("team/app", child.ManifestDigest, OciTestImage.ManifestType, child.Manifest, Token);
            var (index, _) = OciTestImage.Index((child, "linux", "amd64", null));
            await registry.PutManifestAsync("team/app", "multi", OciTestImage.IndexType, index, Token);

            return 0;
        });
        var signed = await PushImageAsync("reg", "team/app", "signed");
        var signature = OciTestImage.Create("sig", subject: signed.ManifestDigest, artifactType: "application/vnd.example.signature");
        await InRepoAsync("reg", async sp =>
        {
            var registry = sp.GetRequiredService<IOciRegistry>();
            await registry.PutBlobAsync("team/app", OciTestImage.Digest(signature.Config), new MemoryStream(signature.Config), Token);
            foreach (var layer in signature.Layers) await registry.PutBlobAsync("team/app", OciTestImage.Digest(layer), new MemoryStream(layer), Token);
            await registry.PutManifestAsync("team/app", signature.ManifestDigest, OciTestImage.ManifestType, signature.Manifest, Token);

            return 0;
        });
        _clock.Advance(TimeSpan.FromDays(100));
        var policy = await SaveAsync(Input("untagged-all", "reg", untagged: 0));

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(0, run.Deleted);
        Assert.True(await ManifestExistsAsync("reg", "team/app", child.ManifestDigest));
        Assert.True(await ManifestExistsAsync("reg", "team/app", signature.ManifestDigest));
    }

    [Fact]
    public async Task ADockerImageHoldKeepsItsTagsAndItsUntaggedManifests()
    {
        await CreateAsync("reg", RepositoryFormat.Docker);
        var image = await PushImageAsync("reg", "legal/app", "v1");
        await PushImageAsync("reg", "legal/app", "v2");
        await PushImageAsync("reg", "free/app", "v1");
        _clock.Advance(TimeSpan.FromDays(60));
        await HoldAsync("reg", "LEGAL/App");
        var policy = await SaveAsync(Input("docker-all", "reg", olderThan: 1, untagged: 0));

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "free/app v1" }, Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "legal/app v1", "legal/app v2" }, Items(run, RetentionOutcome.Held));
        Assert.Equal(new[] { "v1", "v2" }, await TagsAsync("reg", "legal/app"));
        Assert.True(await ManifestExistsAsync("reg", "legal/app", image.ManifestDigest));
    }

    [Fact]
    public async Task APulledTagCountsAsDownloadedForTheUnusedRule()
    {
        await CreateAsync("reg", RepositoryFormat.Docker);
        await PushImageAsync("reg", "team/app", "pulled");
        await PushImageAsync("reg", "team/app", "ignored");
        var policy = await SaveAsync(Input("docker-unused", "reg", unused: 30));
        _clock.Advance(TimeSpan.FromDays(60));
        await InRepoAsync("reg", async sp => await sp.GetRequiredService<IOciRegistry>().GetManifestAsync("team/app", "pulled", Token));
        await _app.Services.GetRequiredService<ArtifactUsageRecorder>().FlushAsync(_app.Services.GetRequiredService<IServiceScopeFactory>(), Token);

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "team/app ignored" }, Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "pulled" }, await TagsAsync("reg", "team/app"));
    }

    // ---- remote caches ----

    [Fact]
    public async Task TheCacheLimitOfARemoteRepositoryEvictsTheLeastRecentlyUsedVersions()
    {
        await InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IRepositoryService>().CreateAsync("npmjs", RepositoryFormat.Npm, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "https://registry.example.test/" }, Token);

            return 0;
        });

        // Cached versions are ordinary rows; the three are 100 bytes each.
        var packageKey = await InRepoAsync("npmjs", async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            var package = new NpmPackage { Name = "cached", Created = _clock.UtcNow, Modified = _clock.UtcNow };
            context.NpmPackages!.Add(package);
            await context.SaveChangesAsync(Token);
            foreach (var (version, days) in new[] { ("1.0.0", 3), ("1.1.0", 2), ("1.2.0", 1) })
            {
                context.NpmVersions!.Add(new NpmVersion { PackageKey = package.Key, Version = version, Integrity = "sha512-x", Shasum = "0", TarballSize = 100, Manifest = "{}", Published = _clock.UtcNow.AddDays(-days) });
            }

            await context.SaveChangesAsync(Token);

            return package.Key;
        });
        Assert.True(packageKey > 0);
        var policy = await SaveAsync(Input("cache", "npmjs", cache: 150));

        var dry = await RunAsync(policy, dryRun: true);
        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "cached 1.0.0", "cached 1.1.0" }, Items(dry, RetentionOutcome.Planned));
        Assert.Equal(Items(dry, RetentionOutcome.Planned), Items(run, RetentionOutcome.Deleted));
        Assert.Equal(new[] { "cached 1.2.0" }, await StoredAsync("npmjs", RepositoryFormat.Npm));
    }

    // ---- scope: project, validation, gate ----

    [Fact]
    public async Task AProjectPolicyAppliesToEveryRepositoryOfTheProjectAndOnlyThose()
    {
        await CreateAsync("libs", RepositoryFormat.NuGet);
        await CreateAsync("other", RepositoryFormat.NuGet);
        foreach (var repository in new[] { "libs", "other" })
        {
            await PushNuGetAsync("Shared.Lib", "1.0.0", repository);
            _clock.Advance(TimeSpan.FromDays(1));
            await PushNuGetAsync("Shared.Lib", "2.0.0", repository);
        }

        await InScopeAsync(async sp =>
        {
            var access = sp.GetRequiredService<IAccessAdministration>();
            await access.CreateProjectAsync("team", null, "tester", Token);
            await access.SetRepositoryProjectAsync("libs", "team", "tester", Token);

            return 0;
        });
        var policy = await SaveAsync(Input("project-keep", repository: null, project: "team", keep: 1));

        var run = await RunAsync(policy, dryRun: false);

        Assert.Equal(new[] { "libs" }, run.Repositories.Select(r => r.Repository).ToArray());
        Assert.Equal(new[] { "Shared.Lib 2.0.0" }, await StoredAsync("libs", RepositoryFormat.NuGet));
        Assert.Equal(2, (await StoredAsync("other", RepositoryFormat.NuGet)).Length);
    }

    [Fact]
    public async Task ARealRunIsRefusedWhileTheServerIsReadOnlyAndADryRunStillReports()
    {
        await PushNuGetAsync("Gate.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        var policy = await SaveAsync(Input("gate", olderThan: 1));
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("test");

        try
        {
            var dry = await RunAsync(policy, dryRun: true);
            await Assert.ThrowsAsync<MaintenanceException>(() => RunAsync(policy, dryRun: false));
            Assert.Throws<MaintenanceException>(() => _app.Services.GetRequiredService<RetentionJobs>().TryStart(policy.Key, dryRun: false, "tester", out _));
            await Assert.ThrowsAsync<MaintenanceException>(() => SaveAsync(Input("another", keep: 1)));

            Assert.Equal(1, dry.Selected);
        }
        finally
        {
            gate.Open();
        }

        Assert.Single(await StoredAsync("default", RepositoryFormat.NuGet));
    }

    [Fact]
    public async Task ADisabledPolicyRefusesARealRunButStillReports()
    {
        await PushNuGetAsync("Off.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        var policy = await SaveAsync(Input("off", olderThan: 1, enabled: false));

        var dry = await RunAsync(policy, dryRun: true);

        Assert.Equal(1, dry.Selected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(policy, dryRun: false));
    }

    [Fact]
    public async Task APolicyThatCannotBeRightIsRefusedWithAMessage()
    {
        await CreateAsync("reg", RepositoryFormat.Docker);
        await SaveAsync(Input("taken", keep: 1));
        var admin = await InScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IRetentionAdministration>()));

        async Task<string> RefusalAsync(RetentionPolicyInput input)
            => (await Assert.ThrowsAsync<RetentionException>(() => InScopeAsync(sp => sp.GetRequiredService<IRetentionAdministration>().SavePolicyAsync(input, "tester", Token)))).Message;

        Assert.Contains("already a policy", await RefusalAsync(Input("TAKEN", keep: 1)));
        Assert.Contains("either one repository or one project", await RefusalAsync(Input("both", project: "p", keep: 1)));
        Assert.Contains("either one repository or one project", await RefusalAsync(Input("neither", repository: null, keep: 1)));
        Assert.Contains("at least one rule", await RefusalAsync(Input("none")));
        Assert.Contains("Keep the newest", await RefusalAsync(Input("zero", keep: 0)));
        Assert.Contains("remote repositories only", await RefusalAsync(Input("cache-local", cache: 10)));
        Assert.Contains("Docker repositories only", await RefusalAsync(Input("gc-nuget", gc: true)));
        Assert.Contains("There is no repository", await RefusalAsync(Input("missing", repository: "nope", keep: 1)));
        Assert.Contains("There is no project", await RefusalAsync(Input("noproject", repository: null, project: "nope", keep: 1)));
        Assert.NotNull(admin);
    }

    [Fact]
    public async Task AHoldNeedsAValidNameForTheFormatAndPlacingItTwiceChangesNothing()
    {
        await CreateAsync("jars", RepositoryFormat.Maven);

        async Task<string> RefusalAsync(string repository, string name, string? version)
            => (await Assert.ThrowsAsync<RetentionException>(() => InScopeAsync(sp => sp.GetRequiredService<IRetentionAdministration>().AddHoldAsync(repository, name, version, null, "tester", Token)))).Message;

        Assert.Contains("not a valid Maven", await RefusalAsync("jars", "no-colon", null));
        Assert.Contains("There is no repository", await RefusalAsync("nope", "a:b", null));
        await HoldAsync("jars", "org.acme:lib", "1.0");
        await HoldAsync("jars", "ORG.ACME:lib", "1.0");

        var holds = await InScopeAsync(sp => sp.GetRequiredService<IRetentionAdministration>().ListHoldsAsync(null, Token));
        Assert.Single(holds);
    }

    [Fact]
    public async Task EveryPolicyAndHoldChangeAndEveryRunIsAudited()
    {
        await PushNuGetAsync("Audit.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        var policy = await SaveAsync(Input("audited", olderThan: 1));
        await HoldAsync("default", "Other.Lib");
        await RunAsync(policy, dryRun: true);
        await RunAsync(policy, dryRun: false);
        var holdKey = (await InScopeAsync(sp => sp.GetRequiredService<IRetentionAdministration>().ListHoldsAsync(null, Token))).Single().Key;
        await InScopeAsync(async sp => await sp.GetRequiredService<IRetentionAdministration>().RemoveHoldAsync(holdKey, "tester", Token));
        await InScopeAsync(async sp => await sp.GetRequiredService<IRetentionAdministration>().DeletePolicyAsync(policy.Key, "tester", Token));

        var entries = await InScopeAsync(sp => sp.GetRequiredService<IAccessAdministration>().AuditAsync(100, Token));
        var actions = entries.Select(e => e.Action).ToList();

        Assert.Contains("retention.policy.create", actions);
        Assert.Contains("retention.hold.add", actions);
        Assert.Contains("retention.run", actions);
        Assert.Contains("retention.hold.remove", actions);
        Assert.Contains("retention.policy.delete", actions);
        Assert.All(entries.Where(e => e.Action!.StartsWith("retention.")), e => Assert.Equal("tester", e.Actor));
        Assert.Contains(_audit.Entries, e => e.Action == "retention.dry-run" && e.Subject == "policy audited");
        Assert.Contains(_audit.Entries, e => e.Action == "retention.run" && e.Details!.StartsWith("Deleted 1 version(s)"));
    }

    [Fact]
    public async Task DeletingARepositoryDropsItsPoliciesHoldsAndDownloadTimes()
    {
        await CreateAsync("web", RepositoryFormat.Npm);
        await SaveAsync(Input("web-policy", "web", keep: 1));
        await HoldAsync("web", "left-pad");
        var recorder = _app.Services.GetRequiredService<ArtifactUsageRecorder>();
        var info = await InScopeAsync(sp => sp.GetRequiredService<IRepositoryService>().FindAsync("web", Token));
        recorder.Touch(info!.Key, "left-pad", "1.0.0");
        await recorder.FlushAsync(_app.Services.GetRequiredService<IServiceScopeFactory>(), Token);

        await InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IRepositoryAdministration>().DeleteAsync("web", Token);

            return 0;
        });

        await InScopeAsync(async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            Assert.Empty(await context.RetentionPolicies!.ToListAsync(Token));
            Assert.Empty(await context.RetentionHolds!.IgnoreQueryFilters().ToListAsync(Token));
            Assert.Empty(await context.ArtifactUsages!.IgnoreQueryFilters().ToListAsync(Token));

            return 0;
        });
    }

    // ---- the schedule ----

    [Fact]
    public async Task TheWorkerStartsOnlyThePoliciesWhoseScheduleIsDueAndWaitsForThem()
    {
        await PushNuGetAsync("Sched.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        var due = await SaveAsync(Input("due", olderThan: 1, schedule: 6));
        var later = await SaveAsync(Input("later", olderThan: 1, schedule: 48));
        await SaveAsync(Input("manual", olderThan: 1));
        await SaveAsync(Input("paused", olderThan: 1, schedule: 6, enabled: false));
        var worker = ActivatorUtilities.CreateInstance<RetentionWorker>(_app.Services);

        Assert.Empty(await worker.RunDueAsync(Token));

        _clock.Advance(TimeSpan.FromHours(7));
        var started = await worker.RunDueAsync(Token);

        Assert.Equal(new[] { due.Key }, started);
        var stored = await InScopeAsync(sp => sp.GetRequiredService<IRetentionAdministration>().GetPolicyAsync(due.Key, Token));
        Assert.Equal(_clock.UtcNow, stored!.Policy.LastRun);
        Assert.Empty(await StoredAsync("default", RepositoryFormat.NuGet));
        Assert.Contains(_audit.Entries, e => e.Actor == "scheduler" && e.Action == "retention.run");

        // Not due again until the schedule elapses once more.
        Assert.Empty(await worker.RunDueAsync(Token));
        _clock.Advance(TimeSpan.FromHours(6));
        Assert.Equal(new[] { due.Key }, await worker.RunDueAsync(Token));
        _clock.Advance(TimeSpan.FromHours(48));
        Assert.Contains(later.Key, await worker.RunDueAsync(Token));
    }

    [Fact]
    public async Task AGateThatClosesDuringARunStillAuditsTheDeletionsAlreadyMade()
    {
        await PushNuGetAsync("Cut.Lib", "1.0.0");
        await PushNuGetAsync("Cut.Lib", "2.0.0");
        await PushNuGetAsync("Cut.Lib", "3.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        var policy = await SaveAsync(Input("cut", olderThan: 1));
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        CloseGateAfterDeletions = 1;

        try
        {
            await Assert.ThrowsAsync<MaintenanceException>(() => RunAsync(policy, dryRun: false));
        }
        finally
        {
            gate.Open();
        }

        Assert.Equal(2, (await StoredAsync("default", RepositoryFormat.NuGet)).Length);
        var entry = Assert.Single(_audit.Entries, e => e.Action == "retention.run");
        Assert.Equal("tester", entry.Actor);
        Assert.Contains("interrupted", entry.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Deleted 1 version(s)", entry.Details);
    }

    private sealed class GateClosingDeletion : IPackageDeletionService
    {
        private readonly IPackageDeletionService _inner;
        private readonly MaintenanceGate _gate;
        private readonly Func<int> _after;

        public GateClosingDeletion(IPackageDeletionService inner, MaintenanceGate gate, Func<int> after)
        {
            _inner = inner;
            _gate = gate;
            _after = after;
        }

        public Task<bool> TryDeletePackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
            => _inner.TryDeletePackageAsync(id, version, cancellationToken);

        public async Task<bool> TryHardDeletePackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
        {
            var done = await _inner.TryHardDeletePackageAsync(id, version, cancellationToken);
            if (done && _after() > 0) _gate.Close("test");

            return done;
        }
    }

    [Fact]
    public async Task TheWorkerWaitsWhileTheServerIsReadOnly()
    {
        await PushNuGetAsync("Quiet.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        await SaveAsync(Input("quiet", olderThan: 1, schedule: 1));
        _clock.Advance(TimeSpan.FromHours(2));
        var worker = ActivatorUtilities.CreateInstance<RetentionWorker>(_app.Services);
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("copy");

        try
        {
            Assert.Empty(await worker.RunDueAsync(Token));
        }
        finally
        {
            gate.Open();
        }

        Assert.Single(await worker.RunDueAsync(Token));
        Assert.Empty(await StoredAsync("default", RepositoryFormat.NuGet));
    }

    [Fact]
    public async Task TheJobServiceKeepsTheReportAndRefusesASecondRunOfTheSamePolicy()
    {
        await PushNuGetAsync("Job.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(10));
        var policy = await SaveAsync(Input("job", olderThan: 1));
        var jobs = _app.Services.GetRequiredService<RetentionJobs>();

        Assert.True(jobs.TryStart(policy.Key, dryRun: true, "tester", out _));
        await jobs.WaitAsync(policy.Key);

        var status = jobs.Get(policy.Key)!;
        Assert.Equal(RetentionJobState.Succeeded, status.State);
        Assert.True(status.DryRun);
        Assert.Equal(1, status.Report!.Selected);
        Assert.Single(await StoredAsync("default", RepositoryFormat.NuGet));

        Assert.True(jobs.TryStart(policy.Key, dryRun: false, "tester", out _));
        await jobs.WaitAsync(policy.Key);
        Assert.Equal(1, jobs.Get(policy.Key)!.Report!.Deleted);

        Assert.True(jobs.TryStart(999_999, dryRun: true, "tester", out _));
        await jobs.WaitAsync(999_999);
        Assert.Equal(RetentionJobState.Failed, jobs.Get(999_999)!.State);
    }

    private sealed class RecordingAudit : IAdminAudit
    {
        public ConcurrentQueue<(string? Actor, string Action, string Subject, string? Details)> Entries { get; } = new();

        public void Record(string? actor, string action, string subject, string? details = null)
            => Entries.Enqueue((actor, action, subject, details));
    }
}
