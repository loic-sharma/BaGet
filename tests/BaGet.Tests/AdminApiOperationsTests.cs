using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// <c>/api/v1</c> for operations, retention and vulnerability scanning: the Operations, Retention* and Scan* pages' operations over HTTP.
/// </summary>
public class AdminApiOperationsTests : IDisposable
{
    private readonly AdminApiHarness _api;
    private readonly SlowOperation _slow = new();
    private readonly GuardedOperation _guarded = new();

    public AdminApiOperationsTests(ITestOutputHelper output)
    {
        _api = new AdminApiHarness(output, configureServices: services =>
        {
            services.AddSingleton<IAdminOperation>(_slow);
            services.AddSingleton<IAdminOperation>(_guarded);
        });
    }

    public void Dispose()
    {
        _slow.Release.TrySetResult(true);
        _api.Dispose();
    }

    private static CancellationToken Token => AdminApiHarness.Token;

    // ---- operations ----

    [Fact]
    public async Task OperationsAreListedStartedOnceAtATimeAndTheirStatusIsRead()
    {
        var list = await _api.GetAsync("/api/v1/operations");
        var slow = list.Json.AsArray().Single(o => o!["name"]!.GetValue<string>() == SlowOperation.OperationName)!;
        Assert.Contains("default", slow["repositories"]!.AsArray().Select(r => r!.GetValue<string>()));
        Assert.Empty(slow["runs"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync($"/api/v1/operations/{SlowOperation.OperationName}/repositories/default")).Status);

        var started = await _api.PostAsync($"/api/v1/operations/{SlowOperation.OperationName}/repositories/default");
        Assert.Equal(HttpStatusCode.Accepted, started.Status);
        Assert.Equal("Running", started.Json["state"]!.GetValue<string>());
        Assert.Equal("root", started.Json["actor"]!.GetValue<string>());

        var again = await _api.PostAsync($"/api/v1/operations/{SlowOperation.OperationName}/repositories/default");
        Assert.Equal(HttpStatusCode.Conflict, again.Status);

        _slow.Release.TrySetResult(true);
        await _api.WithAsync(async p => { await p.GetRequiredService<AdminJobService>().WaitAsync(SlowOperation.OperationName, "default"); return 0; });

        var status = await _api.GetAsync($"/api/v1/operations/{SlowOperation.OperationName}/repositories/default");
        Assert.Equal("Succeeded", status.Json["state"]!.GetValue<string>());
        Assert.Equal("done", status.Json["message"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NotFound, (await _api.PostAsync("/api/v1/operations/unknown/repositories/default")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.PostAsync($"/api/v1/operations/{SlowOperation.OperationName}/repositories/ghost")).Status);
    }

    [Fact]
    public async Task AnOperationThatDeletesDataAsksForThePasswordAndAnyStartIsRefusedWhileReadOnly()
    {
        var url = $"/api/v1/operations/{GuardedOperation.OperationName}/repositories/default";

        Assert.Equal("confirmation-required", (await _api.PostAsync(url)).Code);
        Assert.Equal(0, _guarded.Runs);
        Assert.Equal(HttpStatusCode.Accepted, (await _api.PostAsync(url, password: Password)).Status);

        var gate = _api.App.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("test");
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _api.PostAsync($"/api/v1/operations/{SlowOperation.OperationName}/repositories/default")).Status);
        }
        finally
        {
            gate.Open();
        }
    }

    // ---- retention ----

    private async Task AddVersionsAsync(string id, params string[] versions)
    {
        foreach (var version in versions)
        {
            using var package = TestPackages.Build(id, version);
            await _api.App.AddPackageAsync(package, Token);
        }
    }

    private async Task<string[]> VersionsAsync(string id)
        => await _api.WithAsync(async p => (await p.GetRequiredService<IPackageDatabase>().FindAsync(id, includeUnlisted: true, Token)).Select(x => x.NormalizedVersionString!).OrderBy(v => v, StringComparer.Ordinal).ToArray());

    private static object Policy(string name = "keep-one", bool enabled = true, int? keepLast = 1)
        => new { name, scopeKind = "repository", scopeName = "default", enabled, keepLast };

    [Fact]
    public async Task ARetentionPolicyIsCreatedDryRunRunAndDeletedAndAHoldProtectsAVersion()
    {
        await AddVersionsAsync("Api.Lib", "1.0.0", "2.0.0", "3.0.0");

        Assert.Equal("confirmation-required", (await _api.PostAsync("/api/v1/retention/policies", Policy())).Code);
        var created = await _api.PostAsync("/api/v1/retention/policies", Policy(), password: Password);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var key = created.Json["policy"]!["policy"]!["key"]!.GetValue<int>();
        Assert.Equal("default", created.Json["policy"]!["repository"]!.GetValue<string>());

        var dry = await _api.PostAsync($"/api/v1/retention/policies/{key}/dry-run");
        Assert.Equal(HttpStatusCode.Accepted, dry.Status);
        await _api.WithAsync(async p => { await p.GetRequiredService<RetentionJobs>().WaitAsync(key); return 0; });
        var report = await _api.GetAsync($"/api/v1/retention/policies/{key}/run");
        Assert.True(report.Json["dryRun"]!.GetValue<bool>());
        Assert.Equal(2, report.Json["report"]!["selected"]!.GetValue<int>());
        Assert.Equal(3, (await VersionsAsync("Api.Lib")).Length);

        var hold = await _api.PostAsync("/api/v1/retention/holds", new { repository = "default", name = "Api.Lib", version = "1.0.0", reason = "audit" });
        Assert.Equal(HttpStatusCode.Created, hold.Status);
        var holds = await _api.GetAsync("/api/v1/retention/holds?repository=default");
        Assert.Equal("audit", Assert.Single(holds.Json["items"]!.AsArray())!["reason"]!.GetValue<string>());

        Assert.Equal("confirmation-required", (await _api.PostAsync($"/api/v1/retention/policies/{key}/run")).Code);
        Assert.Equal(HttpStatusCode.Accepted, (await _api.PostAsync($"/api/v1/retention/policies/{key}/run", password: Password)).Status);
        await _api.WithAsync(async p => { await p.GetRequiredService<RetentionJobs>().WaitAsync(key); return 0; });
        Assert.Equal(new[] { "1.0.0", "3.0.0" }, await VersionsAsync("Api.Lib"));

        var holdKey = holds.Json["items"]![0]!["key"]!.GetValue<int>();
        Assert.Equal("confirmation-required", (await _api.DeleteAsync($"/api/v1/retention/holds/{holdKey}")).Code);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync($"/api/v1/retention/holds/{holdKey}", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.DeleteAsync($"/api/v1/retention/holds/{holdKey}", password: Password)).Status);

        var changed = await _api.PutAsync($"/api/v1/retention/policies/{key}", Policy(enabled: false, keepLast: 2), password: Password);
        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.False(changed.Json["policy"]!["policy"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Conflict, (await _api.PostAsync($"/api/v1/retention/policies/{key}/run", password: Password)).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync($"/api/v1/retention/policies/{key}", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync($"/api/v1/retention/policies/{key}")).Status);
        Assert.Contains("retention.policy.create", (await _api.GetAsync("/api/v1/audit?subject=keep-one")).Body);
    }

    [Fact]
    public async Task APolicyWithoutARuleOrWithAnUnknownScopeIsRefused()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/retention/policies", Policy(keepLast: null), password: Password)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/retention/policies", new { name = "x", scopeKind = "repository", scopeName = "ghost", enabled = true, keepLast = 1 }, password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.PostAsync("/api/v1/retention/policies/999/dry-run")).Status);
    }

    // ---- scanning ----

    [Fact]
    public async Task ScanningOverviewPolicyAndScanAreReachableAndAPolicyAsksForThePassword()
    {
        var overview = await _api.GetAsync("/api/v1/scanning");
        Assert.Equal(HttpStatusCode.OK, overview.Status);
        Assert.Contains(overview.Json["repositories"]!.AsArray(), r => r!["repository"]!.GetValue<string>() == "default");

        var policy = new { warnSeverity = 1, blockDownloadSeverity = 3, blockUnscanned = true };
        Assert.Equal("confirmation-required", (await _api.PutAsync("/api/v1/scanning/repositories/default/policy", policy)).Code);
        var saved = await _api.PutAsync("/api/v1/scanning/repositories/default/policy", policy, password: Password);
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal(3, saved.Json["values"]!["blockDownloadSeverity"]!.GetValue<int>());
        Assert.Equal("Repository", saved.Json["from"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsync("/api/v1/scanning/repositories/default/policy", new { warnSeverity = 9 }, password: Password)).Status);

        var dashboard = await _api.GetAsync("/api/v1/scanning/repositories/default");
        Assert.Equal(1, dashboard.Json["ownPolicy"]!["warnSeverity"]!.GetValue<int>());

        var results = await _api.GetAsync("/api/v1/scanning/repositories/default/results?min=0");
        Assert.Equal(HttpStatusCode.OK, results.Status);

        // Scanning is off in this server: the scan is refused with the reason, not silently ignored.
        var scan = await _api.PostAsync("/api/v1/scanning/repositories/default/scan");
        Assert.Equal(HttpStatusCode.Conflict, scan.Status);

        // Empty thresholds remove the policy.
        await _api.PutAsync("/api/v1/scanning/repositories/default/policy", new { }, password: Password);
        Assert.Null((await _api.GetAsync("/api/v1/scanning/repositories/default")).Json["ownPolicy"]);

        await _api.PostAsync("/api/v1/projects", new { name = "acme" });
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/scanning/projects/acme/policy")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PutAsync("/api/v1/scanning/projects/acme/policy", new { blockDeploySeverity = 2 }, password: Password)).Status);
        Assert.Equal(2, (await _api.GetAsync("/api/v1/scanning/projects/acme/policy")).Json["blockDeploySeverity"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/scanning/repositories/ghost")).Status);
    }

    [Fact]
    public async Task AScanRunsAndItsResultsAreListedWhenScanningIsOn()
    {
        await using var harness = await ScanningHarness.StartAsync(TestContext.Current.TestOutputHelper!);
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-api-1", "HIGH", "1.0.1");
        using (var package = TestPackages.Build("Acme.App", "1.0.0"))
        {
            await harness.App.AddPackageAsync(package, Token);
        }

        Task<(HttpStatusCode Status, string Body)> Call(HttpMethod method, string path, string? body = null) => CallAsync(harness, method, path, body);

        var started = await Call(HttpMethod.Post, "/api/v1/scanning/repositories/default/scan", "{}");
        Assert.Equal(HttpStatusCode.Accepted, started.Status);
        await harness.App.Services.GetRequiredService<AdminJobService>().WaitAsync("vulnerability-scan", "default");

        var results = await Call(HttpMethod.Get, "/api/v1/scanning/repositories/default/results?min=0");
        Assert.Equal(HttpStatusCode.OK, results.Status);
        Assert.Contains("acme.app", results.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"highest\":2", results.Body);

        var dashboard = await Call(HttpMethod.Get, "/api/v1/scanning/repositories/default");
        Assert.Contains("\"lastScan\"", dashboard.Body);
    }

    private static async Task<(HttpStatusCode, string)> CallAsync(ScanningHarness harness, HttpMethod method, string path, string? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ScanningHarness.Key);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await harness.Client.SendAsync(request, Token);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    private sealed class SlowOperation : IAdminOperation
    {
        public const string OperationName = "test-slow";

        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => OperationName;

        public string Title => "Slow";

        public string Description => "Waits for the test.";

        public bool IsAvailable(IServiceProvider services) => true;

        public async Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            await Release.Task.WaitAsync(cancellationToken);

            return "done";
        }
    }

    private sealed class GuardedOperation : IAdminOperation
    {
        public const string OperationName = "test-guarded";
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public string Name => OperationName;

        public string Title => "Guarded";

        public string Description => "Deletes something.";

        public bool RequiresConfirmation => true;

        public bool IsAvailable(IServiceProvider services) => true;

        public Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);

            return Task.FromResult("deleted");
        }
    }
}
