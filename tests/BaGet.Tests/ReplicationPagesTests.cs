using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The replication pages and the administration REST API (P9): administrators only, the password where a replication is created, tested,
/// run, resumed, changed or deleted, 503 while the server is read-only, every change audited, the peer's key never shown, and the same
/// operations over <c>/api/v1/replication</c>.
/// </summary>
public class ReplicationPagesTests : IDisposable
{
    private readonly ReplicationHarness _h;
    private readonly Browser _admin;

    public ReplicationPagesTests(ITestOutputHelper output)
    {
        _h = new ReplicationHarness(output);
        _admin = new Browser(_h.Local.App);
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _h.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Dictionary<string, string> Fields(string name, string password = Password, string? apiKey = null, string peerUrl = ReplicationHarness.PeerAddress)
        => new()
        {
            { "Form.Name", name },
            { "Form.Repository", "default" },
            { "Form.Direction", "Push" },
            { "Form.PeerUrl", peerUrl },
            { "Form.PeerRepository", "default" },
            { "Form.ApiKey", apiKey ?? _h.PeerKey },
            { "Form.Enabled", "true" },
            { "Form.Conflict", "TargetWins" },
            { "password", password },
        };

    private async Task<int> CreateAsync(string name)
    {
        using var response = await _admin.PostAsync("admin/replication", Fields(name));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return int.Parse(response.Headers.Location!.ToString().Split('/').Last(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private Task<HttpResponseMessage> PostAsync(int key, string handler, Dictionary<string, string>? fields = null)
        => _admin.PostAsync($"admin/replication/{key}?handler={handler}", fields ?? new Dictionary<string, string> { { "password", Password } }, tokenPage: $"admin/replication/{key}");

    // ---- pages ----

    [Fact]
    public async Task OnlyAdministratorsSeeThePagesAndTheMenuLinksToThem()
    {
        await CreateUserAsync(_h.Local.App, "carol");
        using var carol = new Browser(_h.Local.App);
        await SignInAsync(carol, "carol");
        using var anonymous = new Browser(_h.Local.App);

        foreach (var path in new[] { "admin/replication", "admin/replication/1" })
        {
            using var asUser = await carol.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, asUser.StatusCode);

            using var asAnonymous = await anonymous.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.OK, asAnonymous.StatusCode);
        }

        using var post = await carol.RawPostAsync("admin/replication", Fields("sneaky"));
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.Contains("href=\"/admin/replication\"", await _admin.GetPageAsync("admin/replication"));
    }

    [Fact]
    public async Task AReplicationIsCreatedWithThePasswordListedAndAuditedAndTheKeyIsNeverShown()
    {
        using var wrong = await _admin.PostAsync("admin/replication", Fields("nope", password: "wrong"));
        Assert.Contains("The confirmation is incorrect", await wrong.Content.ReadAsStringAsync(Token));

        var key = await CreateAsync("to-peer");

        var list = await _admin.GetPageAsync("admin/replication");
        Assert.Contains("to-peer", list);
        Assert.DoesNotContain("nope", list);
        Assert.DoesNotContain(_h.PeerKey, list);

        var detail = await _admin.GetPageAsync($"admin/replication/{key}");
        Assert.Contains("Run now", detail);
        Assert.Contains("A peer API key is stored.", detail);
        Assert.DoesNotContain(_h.PeerKey, detail);

        var audit = await _admin.GetPageAsync("admin/audit");
        Assert.Contains("replication.create", audit);
        Assert.DoesNotContain(_h.PeerKey, audit);
    }

    [Fact]
    public async Task ABadReplicationShowsTheMessageAndStaysOnTheForm()
    {
        using var response = await _admin.PostAsync("admin/replication", Fields("bad", peerUrl: "ftp://peer/"));
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("absolute http or https", html);
    }

    [Fact]
    public async Task RunNowAsksForThePasswordCopiesAndLogsAndAFailedRunOffersResume()
    {
        await _h.Local.App.AddPackageAsync(TestPackages.Build("Page.Lib", "1.0.0"), Token);
        var key = await CreateAsync("run-me");
        var jobs = _h.Local.App.Services.GetRequiredService<ReplicationJobs>();

        using (var refused = await PostAsync(key, "Run", new Dictionary<string, string> { { "password", "wrong" } }))
        {
            Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        }

        Assert.Contains("The confirmation is incorrect", await _admin.GetPageAsync($"admin/replication/{key}"));
        Assert.Empty(await _h.RunsAsync(key));

        // The peer is down: the run is logged as failed and the page offers to resume it.
        _h.FailPeer = true;
        using (await PostAsync(key, "Run")) { }
        await jobs.WaitAsync(key);
        var failed = await _admin.GetPageAsync($"admin/replication/{key}");
        Assert.Contains("data-status=\"Failed\"", failed);
        Assert.Contains("could not be reached", failed);
        Assert.Contains("Resume run", failed);

        _h.FailPeer = false;
        using (await PostAsync(key, "Resume")) { }
        await jobs.WaitAsync(key);
        var done = await _admin.GetPageAsync($"admin/replication/{key}");
        Assert.Contains("data-status=\"Succeeded\"", done);
        Assert.DoesNotContain("Resume run", done);
        Assert.Single((await _h.InventoryAsync(_h.Peer)).Items);
        var runs = await _h.RunsAsync(key);
        Assert.Equal("resume", runs[0].Trigger);
        Assert.Equal(runs[1].Key, runs[0].ResumedFrom);
        Assert.Contains("replication.run", await _admin.GetPageAsync("admin/audit"));
    }

    [Fact]
    public async Task TestConnectionNeedsThePasswordSavesNothingAndNamesAWrongKey()
    {
        var key = await CreateAsync("probe");

        using (var good = await PostAsync(key, "Test", Fields("probe")))
        {
            Assert.Contains("Connected: &#x27;default&#x27; is a local NuGet repository", await good.Content.ReadAsStringAsync(Token));
        }

        using (var noPassword = await PostAsync(key, "Test", Fields("probe", password: "wrong")))
        {
            Assert.Contains("The confirmation is incorrect", await noPassword.Content.ReadAsStringAsync(Token));
        }

        using (var bad = await PostAsync(key, "Test", Fields("probe", apiKey: "not-a-key")))
        {
            var html = await bad.Content.ReadAsStringAsync(Token);
            Assert.Contains("refused the API key", html);
            Assert.DoesNotContain("not-a-key", html);
        }

        // The stored key is unchanged by a test with another one typed.
        var view = await _h.Local.WithAsync(s => s.GetRequiredService<IReplicationAdministration>().GetAsync(key, Token));
        Assert.True(view!.HasApiKey);
    }

    [Fact]
    public async Task SavingChangesTheSettingsAndDeletingRemovesTheReplicationAndItsLog()
    {
        var key = await CreateAsync("edit-me");
        var fields = Fields("renamed", apiKey: "");
        fields["Form.ScheduleMinutes"] = "30";
        fields["Form.PropagateDeletes"] = "true";
        using (var save = await PostAsync(key, "Save", fields))
        {
            Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        }

        var page = await _admin.GetPageAsync($"admin/replication/{key}");
        Assert.Contains("renamed", page);
        Assert.Contains("push, every 30 min, deletes follow, target wins", page);
        Assert.True((await _h.Local.WithAsync(s => s.GetRequiredService<IReplicationAdministration>().GetAsync(key, Token)))!.HasApiKey);

        using (var delete = await PostAsync(key, "Delete"))
        {
            Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        }

        using var gone = await _admin.GetAsync($"admin/replication/{key}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task APostIsRefusedWith503WhileTheServerIsReadOnly()
    {
        var gate = _h.Local.App.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("test");
        try
        {
            using var response = await _admin.PostAsync("admin/replication", Fields("closed"));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            gate.Open();
        }
    }

    // ---- REST API ----

    [Fact]
    public async Task TheApiCreatesChangesRunsAndDeletesAReplicationAsThePagesDo()
    {
        await _h.Local.App.AddPackageAsync(TestPackages.Build("Api.Lib", "1.0.0"), Token);
        var body = new
        {
            name = "api-r",
            repository = "default",
            direction = "Push",
            peerUrl = ReplicationHarness.PeerAddress,
            peerRepository = "default",
            apiKey = _h.PeerKey,
            enabled = true,
            conflict = "TargetWins",
        };

        // The page asks for the password; so does the API.
        var unconfirmed = await _h.Local.PostAsync("/api/v1/replication/configs", body);
        Assert.Equal(HttpStatusCode.Forbidden, unconfirmed.Status);
        Assert.Equal("confirmation-required", unconfirmed.Code);

        var created = await _h.Local.PostAsync("/api/v1/replication/configs", body, password: Password);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.DoesNotContain(_h.PeerKey, created.Body);
        var key = created.Json["replication"]!["config"]!["key"]!.GetValue<int>();
        Assert.True(created.Json["replication"]!["hasApiKey"]!.GetValue<bool>());

        var listed = await _h.Local.GetAsync("/api/v1/replication/configs");
        Assert.Equal("api-r", listed.Json["items"]![0]!["replication"]!["config"]!["name"]!.GetValue<string>());

        var test = await _h.Local.PostAsync("/api/v1/replication/test?key=" + key, body, password: Password);
        Assert.True(test.Json["ok"]!.GetValue<bool>(), test.Body);

        var noResume = await _h.Local.PostAsync($"/api/v1/replication/configs/{key}/resume", password: Password);
        Assert.Equal(HttpStatusCode.Conflict, noResume.Status);
        var noStop = await _h.Local.PostAsync($"/api/v1/replication/configs/{key}/stop");
        Assert.Equal(HttpStatusCode.Conflict, noStop.Status);

        var started = await _h.Local.PostAsync($"/api/v1/replication/configs/{key}/run", password: Password);
        Assert.Equal(HttpStatusCode.Accepted, started.Status);
        await _h.Local.App.Services.GetRequiredService<ReplicationJobs>().WaitAsync(key);

        var runs = await _h.Local.GetAsync($"/api/v1/replication/configs/{key}/runs");
        var run = runs.Json["items"]![0]!;
        Assert.Equal("Succeeded", run["status"]!.GetValue<string>());
        Assert.Equal(1, run["transferred"]!.GetValue<int>());
        var one = await _h.Local.GetAsync($"/api/v1/replication/runs/{run["key"]!.GetValue<int>()}");
        Assert.Equal(HttpStatusCode.OK, one.Status);

        var changed = await _h.Local.PutAsync($"/api/v1/replication/configs/{key}", new { name = "api-r", repository = "default", direction = "Push", peerUrl = ReplicationHarness.PeerAddress, peerRepository = "default", enabled = false, scheduleMinutes = 15 }, password: Password);
        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.False(changed.Json["replication"]!["config"]!["enabled"]!.GetValue<bool>());
        Assert.True(changed.Json["replication"]!["hasApiKey"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.NoContent, (await _h.Local.DeleteAsync($"/api/v1/replication/configs/{key}", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.Local.GetAsync($"/api/v1/replication/configs/{key}")).Status);
    }

    [Fact]
    public async Task ADisabledReplicationIsNotStartedByThePageOrTheApi()
    {
        var key = await _h.CreateAsync("off", enabled: false);

        using (await PostAsync(key, "Run")) { }
        Assert.Contains("is disabled: enable it first", await _admin.GetPageAsync($"admin/replication/{key}"));
        Assert.Empty(await _h.RunsAsync(key));

        var api = await _h.Local.PostAsync($"/api/v1/replication/configs/{key}/run", password: Password);
        Assert.Equal(HttpStatusCode.Conflict, api.Status);
        Assert.False(_h.Local.App.Services.GetRequiredService<ReplicationJobs>().IsRunning(key));
    }

    [Fact]
    public async Task TheInventoryEndpointListsWhatARepositoryHoldsForAPeer()
    {
        await _h.Local.App.AddPackageAsync(TestPackages.Build("Inv.Lib", "1.2.3"), Token);

        var full = await _h.Local.GetAsync("/api/v1/replication/inventory/default");
        Assert.Equal(HttpStatusCode.OK, full.Status);
        Assert.Equal("NuGet", full.Json["format"]!.GetValue<string>());
        Assert.Equal("Local", full.Json["kind"]!.GetValue<string>());
        Assert.Equal(1, full.Json["count"]!.GetValue<int>());
        Assert.Equal("1.2.3", full.Json["items"]![0]!["version"]!.GetValue<string>());

        var summary = await _h.Local.GetAsync("/api/v1/replication/inventory/default?summary=true");
        Assert.Equal(1, summary.Json["count"]!.GetValue<int>());
        Assert.Empty(summary.Json["items"]!.AsArray());

        Assert.Equal(HttpStatusCode.NotFound, (await _h.Local.GetAsync("/api/v1/replication/inventory/nowhere")).Status);

        // It lists the whole repository, so a plain user key or no key at all is refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _h.Local.GetAsync("/api/v1/replication/inventory/default", key: "")).Status);
        var alice = await _h.Local.KeyAsync("alice", ApiKeyScopes.All);
        Assert.Equal(HttpStatusCode.Forbidden, (await _h.Local.GetAsync("/api/v1/replication/inventory/default", alice)).Status);
    }
}
