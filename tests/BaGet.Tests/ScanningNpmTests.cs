using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Vulnerability scanning of npm repositories over HTTP (P7): a stored version and its pinned dependencies are checked against a
/// fake OSV, the policy of the repository decides about warnings, downloads and deploys, and a refusal is a 403 with the reason.
/// </summary>
public class ScanningNpmTests
{
    private const string Registry = "/r/web/npm/";
    private const int Low = 0, Moderate = 1, High = 2, Critical = 3;

    private readonly ITestOutputHelper _output;

    public ScanningNpmTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private async Task<ScanningHarness> StartAsync(Dictionary<string, string>? settings = null)
    {
        var harness = await ScanningHarness.StartAsync(_output, settings: settings);
        await harness.App.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);

        return harness;
    }

    private static async Task<HttpResponseMessage> PublishAsync(ScanningHarness harness, string name, string version, IDictionary<string, string>? dependencies = null)
    {
        var tarball = NpmTestPackages.Tarball(name, version, dependencies: dependencies);
        using var request = NpmTestPackages.Json(HttpMethod.Put, NpmTestPackages.Path(Registry, name), NpmTestPackages.PublishBody(tarball, dependencies: dependencies), ScanningHarness.Key);

        return await harness.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> TarballAsync(ScanningHarness harness, string name, string version)
        => harness.Client.GetAsync(Registry + name + "/-/" + name + "-" + version + ".tgz", TestContext.Current.CancellationToken);

    private static async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task AVersionAndItsPinnedDependenciesAreScannedAndTheFindingsStored()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "left-pad", "1.0.0", "GHSA-aaaa-bbbb-cccc", "CRITICAL", "1.3.0");
        harness.Osv.Add("npm", "lib", "2.0.0", "GHSA-own-own-own", "LOW");
        Assert.Equal(HttpStatusCode.Created, (await PublishAsync(harness, "lib", "2.0.0", new Dictionary<string, string> { ["left-pad"] = "1.0.0", ["ranged"] = "^1.2.0" })).StatusCode);

        var result = await harness.ScanAsync("web", "lib", "2.0.0");

        Assert.NotNull(result);
        Assert.Null(result!.Error);
        Assert.Equal("osv", result.Scanner);
        Assert.Equal(Critical, result.Highest);
        Assert.Equal(2, result.FindingCount);
        var found = (await harness.ResultAsync("web", "lib", "2.0.0"))!;
        var dependency = found.Findings!.First();
        Assert.Equal("GHSA-aaaa-bbbb-cccc", dependency.VulnerabilityId);
        Assert.Equal("left-pad", dependency.Component);
        Assert.Equal("1.0.0", dependency.ComponentVersion);
        Assert.Equal(Critical, dependency.Severity);
        Assert.Equal("https://example.test/advisories/GHSA-aaaa-bbbb-cccc", dependency.AdvisoryUrl);
        Assert.Equal("1.3.0", dependency.FixedVersion);
        Assert.Equal("lib", found.Findings!.Last().Component);

        // The range "^1.2.0" is not one version, so it was never asked about.
        Assert.Equal(2, harness.Osv.QueriedPackages);
    }

    [Fact]
    public async Task APublishedVersionIsScannedInTheBackground()
    {
        await using var harness = await StartAsync(new Dictionary<string, string> { { "Scanning:ScanOnDeploy", "true" } });
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-bg", "HIGH");
        await PublishAsync(harness, "lib", "1.0.0");

        ScanResult? result = null;
        for (var i = 0; i < 100 && result?.ScannedAt == null; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            result = await harness.ResultAsync("web", "lib", "1.0.0");
        }

        Assert.NotNull(result?.ScannedAt);
        Assert.Equal(High, result!.Highest);
    }

    [Fact]
    public async Task StoringAnArtifactAgainForgetsTheVerdictOfTheEarlierOneSoItIsNotTakenForScanned()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-old", "CRITICAL");
        await PublishAsync(harness, "lib", "1.0.0");
        await harness.ScanAsync("web", "lib", "1.0.0");
        Assert.NotNull(await harness.ResultAsync("web", "lib", "1.0.0"));

        // What every store path calls once the new bytes are stored (a re-publish after a delete, a Maven redeploy).
        await harness.RunAsync("web", async sp =>
        {
            await sp.GetRequiredService<IScanGate>().RegisterStoredAsync("lib", "1.0.0", TestContext.Current.CancellationToken);

            return 0;
        });

        Assert.Null(await harness.ResultAsync("web", "lib", "1.0.0"));
    }

    [Fact]
    public async Task APublishAfterAnUnpublishOfTheSameVersionStartsWithoutTheEarlierVerdict()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-old", "CRITICAL");
        await PublishAsync(harness, "lib", "1.0.0");
        await harness.ScanAsync("web", "lib", "1.0.0");
        Assert.NotNull(await harness.ResultAsync("web", "lib", "1.0.0"));

        using (var remove = NpmTestPackages.Json(HttpMethod.Delete, NpmTestPackages.Path(Registry, "lib") + "/-rev/1-abc", token: ScanningHarness.Key))
        {
            Assert.Equal(HttpStatusCode.OK, (await harness.Client.SendAsync(remove, TestContext.Current.CancellationToken)).StatusCode);
        }

        // The tarball is new bytes: the verdict about the deleted one must be gone, whichever step dropped it.
        using var again = await PublishAsync(harness, "lib", "1.0.0");
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Null(await harness.ResultAsync("web", "lib", "1.0.0"));
    }

    [Fact]
    public async Task ACleanVersionIsRecordedAsScannedWithNoFindings()
    {
        await using var harness = await StartAsync();
        await PublishAsync(harness, "lib", "1.0.0");

        var result = (await harness.ScanAsync("web", "lib", "1.0.0"))!;

        Assert.NotNull(result.ScannedAt);
        Assert.Equal(-1, result.Highest);
        Assert.Equal(0, result.FindingCount);
    }

    [Fact]
    public async Task ABlockDownloadPolicyRefusesATarballWithAFindingAtOrAboveItsSeverityAndSaysWhy()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-high-one", "HIGH");
        harness.Osv.Add("npm", "lib", "2.0.0", "GHSA-moderate", "MODERATE");
        await PublishAsync(harness, "lib", "1.0.0");
        await PublishAsync(harness, "lib", "2.0.0");
        await PublishAsync(harness, "lib", "3.0.0");
        foreach (var version in new[] { "1.0.0", "2.0.0", "3.0.0" }) await harness.ScanAsync("web", "lib", version);
        await harness.SetPolicyAsync("web", blockDownload: High);

        using var blocked = await TarballAsync(harness, "lib", "1.0.0");
        using var below = await TarballAsync(harness, "lib", "2.0.0");
        using var clean = await TarballAsync(harness, "lib", "3.0.0");

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var body = JsonNode.Parse(await TextAsync(blocked))!;
        var reason = (string)body["error"]!;
        Assert.Contains("GHSA-high-one", reason);
        Assert.Contains("lib 1.0.0", reason);
        Assert.Contains("web", reason);
        Assert.Equal(HttpStatusCode.OK, below.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clean.StatusCode);

        // The packument stays readable, so a client sees the versions and the reason when it asks for one.
        using var packument = await harness.Client.GetAsync(Registry + "lib", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, packument.StatusCode);
    }

    [Fact]
    public async Task AWarnOnlyPolicyBlocksNothing()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-warn", "CRITICAL");
        await PublishAsync(harness, "lib", "1.0.0");
        await harness.ScanAsync("web", "lib", "1.0.0");
        await harness.SetPolicyAsync("web", warn: Low);

        using var tarball = await TarballAsync(harness, "lib", "1.0.0");

        Assert.Equal(HttpStatusCode.OK, tarball.StatusCode);
    }

    [Fact]
    public async Task AVersionIsServedAgainOnceItsPolicyIsLifted()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-lift", "CRITICAL");
        await PublishAsync(harness, "lib", "1.0.0");
        await harness.ScanAsync("web", "lib", "1.0.0");
        await harness.SetPolicyAsync("web", blockDownload: Critical);
        using (var blocked = await TarballAsync(harness, "lib", "1.0.0")) Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        await harness.SetPolicyAsync("web");

        using var served = await TarballAsync(harness, "lib", "1.0.0");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Fact]
    public async Task ABlockDeployPolicyRefusesAPublishWhoseDependencyIsVulnerableAndStoresNothing()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "left-pad", "1.0.0", "GHSA-deploy", "HIGH");
        await harness.SetPolicyAsync("web", blockDeploy: High);

        using var refused = await PublishAsync(harness, "app", "1.0.0", new Dictionary<string, string> { ["left-pad"] = "1.0.0" });
        using var accepted = await PublishAsync(harness, "app", "1.0.1", new Dictionary<string, string> { ["left-pad"] = "1.0.1" });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var reason = (string)JsonNode.Parse(await TextAsync(refused))!["error"]!;
        Assert.Contains("GHSA-deploy", reason);
        Assert.Contains("left-pad 1.0.0", reason);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        using var packument = await harness.Client.GetAsync(Registry + "app", TestContext.Current.CancellationToken);
        var versions = ((JsonObject)JsonNode.Parse(await TextAsync(packument))!["versions"]!).Select(v => v.Key).ToList();
        Assert.Equal(new[] { "1.0.1" }, versions);
    }

    [Fact]
    public async Task ADeployIsNotRefusedBelowTheThresholdOrWithoutAPolicy()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "left-pad", "1.0.0", "GHSA-low", "LOW");

        using var noPolicy = await PublishAsync(harness, "app", "1.0.0", new Dictionary<string, string> { ["left-pad"] = "1.0.0" });
        await harness.SetPolicyAsync("web", blockDeploy: High);
        using var belowThreshold = await PublishAsync(harness, "app", "1.0.1", new Dictionary<string, string> { ["left-pad"] = "1.0.0" });

        Assert.Equal(HttpStatusCode.Created, noPolicy.StatusCode);
        Assert.Equal(HttpStatusCode.Created, belowThreshold.StatusCode);
    }

    [Fact]
    public async Task BlockUnscannedRefusesAStoredVersionNoScanHasSeenAndQueuesIt()
    {
        await using var harness = await StartAsync();
        await PublishAsync(harness, "lib", "1.0.0");
        await harness.SetPolicyAsync("web", blockUnscanned: true);
        // Whatever the background worker has done so far, the result is dropped so the version is unscanned again.
        await harness.RunAsync("web", async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            context.ScanResults!.RemoveRange(context.ScanResults!.ToList());
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return 0;
        });

        using var blocked = await TarballAsync(harness, "lib", "1.0.0");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("not been scanned", (string)JsonNode.Parse(await TextAsync(blocked))!["error"]!);

        // The refusal queued a scan, so a moment later the version can be downloaded.
        HttpStatusCode status = HttpStatusCode.Forbidden;
        for (var i = 0; i < 100 && status != HttpStatusCode.OK; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            using var again = await TarballAsync(harness, "lib", "1.0.0");
            status = again.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task BlockUnscannedOfAMemberAppliesThroughAVirtualRepository()
    {
        await using var harness = await StartAsync();
        await PublishAsync(harness, "lib", "1.0.0");
        using (var scope = harness.App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
                "all",
                new VirtualRepositorySettings { Members = new List<string> { "web" }, DeployRepository = "web" },
                TestContext.Current.CancellationToken,
                actor: null,
                anonymousRead: true,
                RepositoryFormat.Npm);
        }

        await harness.SetPolicyAsync("web", blockUnscanned: true);
        await harness.RunAsync("web", async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            context.ScanResults!.RemoveRange(context.ScanResults!.ToList());
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return 0;
        });

        // The refusal queues a scan, so only the first request sees the version unscanned.
        using var viaVirtual = await harness.Client.GetAsync("/r/all/npm/lib/-/lib-1.0.0.tgz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, viaVirtual.StatusCode);
    }

    [Fact]
    public async Task APolicyOfTheProjectAppliesToItsRepositoriesUnlessTheRepositoryHasItsOwn()
    {
        await using var harness = await StartAsync();
        await harness.App.CreateNpmRepositoryAsync("other", cancellationToken: TestContext.Current.CancellationToken);
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-project", "HIGH");

        var effective = await harness.RunAsync(null, async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            var project = new Project { Name = "team" };
            context.Projects!.Add(project);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            var web = await context.Repositories!.FirstAsync(r => r.Name == "web", TestContext.Current.CancellationToken);
            var other = await context.Repositories!.FirstAsync(r => r.Name == "other", TestContext.Current.CancellationToken);
            web.ProjectKey = project.Key;
            other.ProjectKey = project.Key;
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var policies = sp.GetRequiredService<IScanPolicyService>();
            await policies.SaveAsync(ScanPolicyScope.Project, project.Key, new ScanPolicyValues(null, High, null, false), "tester", TestContext.Current.CancellationToken);
            await policies.SaveAsync(ScanPolicyScope.Repository, other.Key, new ScanPolicyValues(null, null, null, true), "tester", TestContext.Current.CancellationToken);

            return (Web: await policies.GetEffectiveAsync(web.Key, TestContext.Current.CancellationToken), Other: await policies.GetEffectiveAsync(other.Key, TestContext.Current.CancellationToken));
        });

        Assert.Equal(ScanPolicyScope.Project, effective.Web.From);
        Assert.Equal("team", effective.Web.Name);
        Assert.Equal(High, effective.Web.Values.BlockDownloadSeverity);
        Assert.Equal(ScanPolicyScope.Repository, effective.Other.From);
        Assert.Null(effective.Other.Values.BlockDownloadSeverity);
        Assert.True(effective.Other.Values.BlockUnscanned);
    }

    [Fact]
    public async Task ARescanLearnsOfAnAdvisoryPublishedAfterTheFirstScan()
    {
        await using var harness = await StartAsync();
        await PublishAsync(harness, "lib", "1.0.0");
        Assert.Equal(0, (await harness.ScanAsync("web", "lib", "1.0.0"))!.FindingCount);

        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-late", "HIGH");
        await harness.RunAsync(null, sp =>
        {
            sp.GetRequiredService<OsvState>().Clear();

            return Task.FromResult(0);
        });
        var result = (await harness.ScanAsync("web", "lib", "1.0.0"))!;

        Assert.Equal(1, result.FindingCount);
        Assert.Equal(High, result.Highest);
    }

    [Fact]
    public async Task AnOsvAnswerIsRememberedSoTheSameComponentIsAskedOnce()
    {
        await using var harness = await StartAsync();
        await PublishAsync(harness, "lib", "1.0.0");
        await harness.ScanAsync("web", "lib", "1.0.0");
        var batches = harness.Osv.Batches;

        await harness.ScanAsync("web", "lib", "1.0.0");

        Assert.Equal(batches, harness.Osv.Batches);
    }

    [Fact]
    public async Task AnOsvThatIsDownLeavesAFailedResultAndNeverBlocksADeploy()
    {
        await using var harness = await StartAsync();
        await PublishAsync(harness, "lib", "1.0.0");
        harness.Osv.Down = true;
        await harness.SetPolicyAsync("web", blockDeploy: Low);

        var failed = (await harness.ScanAsync("web", "lib", "1.0.0"))!;
        using var deploy = await PublishAsync(harness, "lib", "1.0.1");

        Assert.Null(failed.ScannedAt);
        Assert.Contains("OSV", failed.Error);
        Assert.Equal(HttpStatusCode.Created, deploy.StatusCode);
    }

    [Fact]
    public async Task ARepositorySweepScansEveryVersionDropsResultsOfDeletedOnesAndSkipsFreshOnes()
    {
        await using var harness = await StartAsync();
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-sweep", "HIGH");
        await PublishAsync(harness, "lib", "1.0.0");
        await PublishAsync(harness, "lib", "1.0.1");
        await harness.RunAsync("web", async sp =>
        {
            var context = sp.GetRequiredService<IContext>();
            context.ScanResults!.Add(new ScanResult { Name = "gone", Version = "9.9.9", Scanner = "osv", Attempted = System.DateTime.UtcNow, ScannedAt = System.DateTime.UtcNow });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            return 0;
        });

        var report = await harness.RunAsync("web", sp => sp.GetRequiredService<IScanService>().ScanRepositoryAsync(onlyStale: false, TestContext.Current.CancellationToken));

        Assert.Equal(2, report.Scanned);
        Assert.Equal(1, report.WithFindings);
        Assert.Equal(1, report.Removed);
        Assert.Null(await harness.ResultAsync("web", "gone", "9.9.9"));
        var summary = await harness.RunAsync("web", sp => sp.GetRequiredService<IScanService>().SummarizeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, summary.Artifacts);
        Assert.Equal(0, summary.Unscanned);
        Assert.Equal(1, summary.Clean);
        Assert.Equal(1, summary.BySeverity[High]);
    }

    [Fact]
    public async Task ScanningOffRefusesToScanAndBlocksNothing()
    {
        await using var harness = await StartAsync(new Dictionary<string, string> { { "Scanning:Enabled", "false" } });
        harness.Osv.Add("npm", "lib", "1.0.0", "GHSA-off", "CRITICAL");
        await PublishAsync(harness, "lib", "1.0.0");
        await harness.SetPolicyAsync("web", blockDownload: Low, blockDeploy: Low, blockUnscanned: true);

        await Assert.ThrowsAsync<ScanUnavailableException>(() => harness.ScanAsync("web", "lib", "1.0.0"));
        using var tarball = await TarballAsync(harness, "lib", "1.0.0");
        using var deploy = await PublishAsync(harness, "lib", "1.0.1");

        Assert.Equal(HttpStatusCode.OK, tarball.StatusCode);
        Assert.Equal(HttpStatusCode.Created, deploy.StatusCode);
        Assert.Equal(0, harness.Osv.Batches);
    }

    [Fact]
    public async Task AnOfflineCopyOfTheOsvDataScansWithoutAskingTheNetwork()
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("osv-offline").FullName;
        try
        {
            await using var source = await FakeOsvServer.StartAsync();
            source.Add("npm", "left-pad", "1.0.0", "GHSA-offline", "CRITICAL", "1.3.0");
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory, "GHSA-offline.json"), source.RecordOf("GHSA-offline").ToJsonString(), TestContext.Current.CancellationToken);
            await using var harness = await StartAsync(new Dictionary<string, string> { { "Scanning:OsvOfflinePath", directory } });
            await PublishAsync(harness, "lib", "2.0.0", new Dictionary<string, string> { ["left-pad"] = "1.0.0" });

            var result = (await harness.ScanAsync("web", "lib", "2.0.0"))!;

            Assert.Null(result.Error);
            Assert.Equal(Critical, result.Highest);
            var finding = (await harness.ResultAsync("web", "lib", "2.0.0"))!.Findings!.Single();
            Assert.Equal("GHSA-offline", finding.VulnerabilityId);
            Assert.Equal("1.3.0", finding.FixedVersion);
            Assert.Equal(0, harness.Osv.Batches);
            Assert.Equal(0, harness.Osv.Lookups);
        }
        finally
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AReadOnlyDatabaseRefusesAScan()
    {
        await using var harness = await StartAsync();
        await PublishAsync(harness, "lib", "1.0.0");
        var gate = harness.App.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("copy in progress");

        await Assert.ThrowsAsync<MaintenanceException>(() => harness.ScanAsync("web", "lib", "1.0.0"));
    }
}
