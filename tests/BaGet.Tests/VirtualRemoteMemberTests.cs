using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// A virtual repository with a remote member: <c>loc</c> (local) and <c>up</c> (a remote repository over an in-memory
/// upstream), <c>v</c> = [loc, up]. The first member that has a version answers; a remote member fetches and stores as it
/// does when read directly, and a remote member that fails is skipped. A second class covers the default repository's
/// mirror (<c>Mirror:Enabled</c>), which keeps falling through to the mirror's remote repository exactly as before.
/// </summary>
public class VirtualRemoteMemberTests : IDisposable
{
    private const string Nupkg = "v3/package/testdata/1.2.3/testdata.1.2.3.nupkg";
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly BaGetApplication _upstream;
    private readonly BaGetApplication _app;
    private readonly ForwardingRemoteHttpClientFactory _wire;
    private readonly HttpClient _client;

    public VirtualRemoteMemberTests(ITestOutputHelper output)
    {
        _upstream = new BaGetApplication(output);
        _wire = new ForwardingRemoteHttpClientFactory(_upstream.CreateClient());
        _app = new BaGetApplication(output, configureServices: services => services.AddSingleton<IRemoteHttpClientFactory>(_wire));
        _client = _app.CreateClient();

        _app.CreateRepositoryAsync("loc").GetAwaiter().GetResult();
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync("up", RepositoryFormat.NuGet, RepositoryKind.Remote, new RemoteRepositorySettings { Url = "http://localhost/v3/index.json" }, Cancel)
            .GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
            .CreateAsync("v", new VirtualRepositorySettings { Members = { "loc", "up" }, DeployRepository = "loc" }, Cancel)
            .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _upstream.Dispose();
    }

    private async Task SeedUpstreamAsync()
    {
        using var package = TestResources.GetResourceStream(TestResources.Package);
        await _upstream.AddPackageAsync(package, Cancel);
    }

    private async Task<int> StoredAsync(string repository)
    {
        using var scope = _app.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().FindAsync(repository, Cancel))!.PackageCount;
    }

    private async Task<HttpStatusCode> GetAsync(string path)
    {
        using var response = await _client.GetAsync(path, Cancel);

        return response.StatusCode;
    }

    [Fact]
    public async Task APackageOnlyTheUpstreamHasIsFetchedStoredInTheRemoteMemberAndServedFromThereAfterwards()
    {
        await SeedUpstreamAsync();

        Assert.Equal(HttpStatusCode.OK, await GetAsync("r/v/" + Nupkg));
        Assert.Equal(1, await StoredAsync("up"));
        Assert.Equal(0, await StoredAsync("loc"));
        var fetches = _wire.Count(".nupkg");
        Assert.Equal(1, fetches);

        // Served from the stored copy now, even with the upstream down.
        _wire.Down = true;
        Assert.Equal(HttpStatusCode.OK, await GetAsync("r/v/" + Nupkg));
        Assert.Equal(fetches, _wire.Count(".nupkg"));
    }

    [Theory]
    [InlineData("r/up/")]
    [InlineData("r/v/")]
    public async Task AFetchRefusedByTheClosedGateIsA503NotA404AndNoMemberIsSkipped(string prefix)
    {
        await SeedUpstreamAsync();
        _app.Services.GetRequiredService<MaintenanceGate>().Close("Copying the database");

        using var response = await _client.GetAsync(prefix + Nupkg, Cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.RetryAfter != null, "Retry-After is missing.");
        Assert.Equal(0, await StoredAsync("up"));
    }

    [Fact]
    public async Task ALocalMemberAheadOfTheRemoteOneAnswersWithoutAskingTheUpstreamForThatVersion()
    {
        await SeedUpstreamAsync();
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _app.AddPackageAsync(package, Cancel, "loc");
        }

        Assert.Equal(HttpStatusCode.OK, await GetAsync("r/v/" + Nupkg));
        Assert.Equal(0, _wire.Count(".nupkg"));
        Assert.Equal(0, await StoredAsync("up"));
    }

    [Fact]
    public async Task ARemoteMemberThatFailsIsSkippedSoALaterMemberCanAnswer()
    {
        // Order the unreachable remote member first.
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
                .UpdateAsync("v", new VirtualRepositorySettings { Members = { "up", "loc" }, DeployRepository = "loc" }, Cancel);
        }

        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _app.AddPackageAsync(package, Cancel, "loc");
        }

        _wire.Down = true;
        Assert.Equal(HttpStatusCode.OK, await GetAsync("r/v/" + Nupkg));
        Assert.Equal(HttpStatusCode.OK, await GetAsync("r/v/v3/registration/testdata/index.json"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync("r/v/v3/package/testdata/index.json"));
    }

    [Fact]
    public async Task TheVersionListMergesTheUpstreamsVersionsWithTheLocalOnes()
    {
        await SeedUpstreamAsync();
        using (var package = TestPackages.Build("TestData", "9.0.0"))
        {
            await _app.AddPackageAsync(package, Cancel, "loc");
        }

        using var response = await _client.GetAsync("r/v/v3/package/testdata/index.json", Cancel);
        var json = await response.Content.ReadAsStringAsync(Cancel);

        Assert.Contains("1.2.3", json);
        Assert.Contains("9.0.0", json);
    }

    [Fact]
    public async Task ThePreviewAsksTheUpstreamOnlyWhenNoEarlierMemberAnswers()
    {
        await SeedUpstreamAsync();

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>();

        var before = await admin.PreviewAsync("v", "TestData", NuGetVersion.Parse("1.2.3"), Cancel);
        Assert.Equal("up", before.Answers!.Name);
        Assert.Equal(new[] { ResolutionOutcome.NotFound, ResolutionOutcome.WouldFetch }, before.Steps.Select(s => s.Outcome));
        Assert.Equal(0, await StoredAsync("up"));

        // After the fetch the remote member stores it.
        Assert.Equal(HttpStatusCode.OK, await GetAsync("r/v/" + Nupkg));
        var after = await admin.PreviewAsync("v", "TestData", NuGetVersion.Parse("1.2.3"), Cancel);
        Assert.Equal(ResolutionOutcome.Stored, after.Steps.Last().Outcome);

        // A local copy ahead of it: the remote member is not asked at all.
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _app.AddPackageAsync(package, Cancel, "loc");
        }

        var requestsBefore = _wire.Requests.Length;
        var ahead = await admin.PreviewAsync("v", "TestData", NuGetVersion.Parse("1.2.3"), Cancel);
        Assert.Equal("loc", ahead.Answers!.Name);
        Assert.Equal(ResolutionOutcome.Stored, ahead.Steps[0].Outcome);
        Assert.Equal(requestsBefore, _wire.Requests.Length);
    }

    [Fact]
    public async Task ThePreviewReportsAnOfflineRemoteMemberAsOfflineAndDoesNotAskIt()
    {
        await SeedUpstreamAsync();
        using var scope = _app.Services.CreateScope();
        var remotes = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var settings = (await remotes.FindAsync("up", Cancel))!.Settings;
        settings.Offline = true;
        await remotes.UpdateAsync("up", settings, clearPassword: false, Cancel);
        var requestsBefore = _wire.Requests.Length;

        var preview = await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
            .PreviewAsync("v", "TestData", NuGetVersion.Parse("1.2.3"), Cancel);

        Assert.Null(preview.Answers);
        Assert.Equal(ResolutionOutcome.Offline, preview.Steps.Last().Outcome);
        Assert.Equal(requestsBefore, _wire.Requests.Length);
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync("r/v/" + Nupkg));
    }

    [Fact]
    public async Task ThePreviewHonoursARememberedNotFoundLikeARealDownload()
    {
        // A real request for a version the upstream does not have remembers the 404 for the negative period...
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync("r/up/" + Nupkg));
        await SeedUpstreamAsync();
        var requestsBefore = _wire.Requests.Length;

        // ...and so answers 404 again without asking; the preview must say the same instead of finding it upstream.
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync("r/up/" + Nupkg));
        using var scope = _app.Services.CreateScope();
        var preview = await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
            .PreviewAsync("v", "TestData", NuGetVersion.Parse("1.2.3"), Cancel);

        Assert.Null(preview.Answers);
        Assert.Equal(ResolutionOutcome.NotFound, preview.Steps.Last().Outcome);
        Assert.Equal(requestsBefore, _wire.Requests.Length);
    }

    [Fact]
    public async Task AnUnreachableUpstreamIsReportedByThePreviewAndTheRestStillAnswers()
    {
        _wire.Down = true;

        using var scope = _app.Services.CreateScope();
        var preview = await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
            .PreviewAsync("v", "TestData", NuGetVersion.Parse("1.2.3"), Cancel);

        Assert.Null(preview.Answers);
        Assert.Equal(ResolutionOutcome.Unavailable, preview.Steps.Last().Outcome);
    }
}

/// <summary>
/// With <c>Mirror:Enabled</c> the default repository fetches through the <c>mirror</c> remote repository and stores what it
/// fetches in its own rows (so <c>default</c> behaves as a virtual repository of [its packages, the mirror] that keeps its
/// URL). Reading <c>default</c> through a virtual repository therefore falls through to the mirror exactly as reading
/// it directly does.
/// </summary>
public class VirtualDefaultMirrorTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly BaGetApplication _upstream;
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public VirtualDefaultMirrorTests(ITestOutputHelper output)
    {
        _upstream = new BaGetApplication(output);
        _app = new BaGetApplication(output, _upstream.CreateClient());
        _client = _app.CreateClient();

        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
            .CreateAsync("v", new VirtualRepositorySettings { Members = { "default" }, DeployRepository = "default" }, Cancel)
            .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _upstream.Dispose();
    }

    [Fact]
    public async Task DefaultFallsThroughToTheMirrorWhenReadThroughAVirtualRepository()
    {
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _upstream.AddPackageAsync(package, Cancel);
        }

        const string nupkg = "v3/package/testdata/1.2.3/testdata.1.2.3.nupkg";
        using var response = await _client.GetAsync("r/v/" + nupkg, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Stored in default's own rows, as when default is read directly; the mirror repository itself holds nothing.
        using var scope = _app.Services.CreateScope();
        var summaries = await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().ListAsync(Cancel);
        Assert.Equal(1, summaries.Single(s => s.Name == "default").PackageCount);
        Assert.Equal(0, summaries.Single(s => s.Name == Repository.MirrorName).PackageCount);
        Assert.Equal(0, summaries.Single(s => s.Name == "v").PackageCount);

        using var direct = await _client.GetAsync(nupkg, Cancel);
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
    }

    [Fact]
    public async Task ThePreviewAsksDefaultsMirrorLikeARealRequestDoes()
    {
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _upstream.AddPackageAsync(package, Cancel);
        }

        using var scope = _app.Services.CreateScope();
        var preview = await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
            .PreviewAsync("v", "TestData", NuGetVersion.Parse("1.2.3"), Cancel);

        // default is a local repository but falls through to the mirror, so a request would fetch it.
        Assert.Equal("default", preview.Answers!.Name);
        Assert.Equal(ResolutionOutcome.WouldFetch, preview.Steps.Single().Outcome);

        using var response = await _client.GetAsync("r/v/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheMirrorRepositoryCanBeAMemberOfAVirtualRepositoryOfItsOwn()
    {
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await _upstream.AddPackageAsync(package, Cancel);
        }

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
                .UpdateAsync("v", new VirtualRepositorySettings { Members = { "default", Repository.MirrorName }, DeployRepository = "default" }, Cancel);
        }

        // default (mirror-backed) answers first and stores it; the mirror repository is never needed.
        using var response = await _client.GetAsync("r/v/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
