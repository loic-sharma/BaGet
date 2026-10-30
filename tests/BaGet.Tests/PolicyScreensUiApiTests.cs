using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The JSON twins (<c>/api/ui/admin/retention</c>, <c>/scanning</c>, <c>/replication</c>) of the retention, scanning and replication screens: who may call
/// them (anonymous / user / administrator cookie (never accepted) / <c>admin:api</c> key / other keys), the read-only gate, the password, that the
/// answers carry what the pages show, and that no answer carries a secret (the peer API key of a replication, the scanner's registry credential).
/// </summary>
public class PolicyScreensUiApiTests : IDisposable
{
    private const string GlobalKey = "global-key";
    private const string PeerKey = "peer-api-key-0123456789";
    private const string TrivySecret = "trivy-registry-secret-value";

    private readonly BaGetApplication _app;
    private readonly Browser _anonymous;
    private readonly Browser _alice;
    private readonly Browser _root;
    private string _adminKey = string.Empty;
    private string _pushOnlyAdminKey = string.Empty;
    private string _aliceKey = string.Empty;
    private string _limitedAdminKey = string.Empty;

    public PolicyScreensUiApiTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Accounts:Enabled", "true" },
                { "ApiKey", GlobalKey },
                { "Scanning:TrivyRegistryPassword", TrivySecret },
            });
        _anonymous = new Browser(_app);
        _alice = new Browser(_app);
        _root = new Browser(_app);
        SetUpAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _anonymous.Dispose();
        _alice.Dispose();
        _root.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task SetUpAsync()
    {
        await CreateUserAsync(_app, "alice", email: "alice@example.com");
        await CreateUserAsync(_app, "root", administrator: true);
        await SignInAsync(_alice, "alice");
        await SignInAsync(_root, "root");

        var defaultKey = (await _app.Services.GetRequiredService<IRepositoryService>().FindAsync("default", Token))!.Key;
        _adminKey = await KeyAsync("root", new[] { ApiKeyScopes.Admin });
        _pushOnlyAdminKey = await KeyAsync("root", new[] { ApiKeyScopes.Push });
        _limitedAdminKey = await KeyAsync("root", new[] { ApiKeyScopes.Admin }, new[] { defaultKey });
        _aliceKey = await KeyAsync("alice", new[] { ApiKeyScopes.Push });
    }

    private async Task<string> KeyAsync(string user, string[] scopes, int[]? repositories = null)
    {
        using var scope = _app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync((await FindAsync(_app, user))!.Id, "test key of " + user, scopes, "*", null, repositories, Token);
    }

    private sealed record Answer(HttpStatusCode Status, JsonNode? Json, string Body);

    private async Task<Answer> SendAsync(Browser browser, HttpMethod method, string path, object? body = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (method != HttpMethod.Get)
        {
            var json = body == null ? "{}" : System.Text.Json.JsonSerializer.Serialize(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await browser.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync(Token);

        return new Answer(response.StatusCode, text.Length > 0 && text[0] == '{' ? JsonNode.Parse(text) : null, text);
    }

    private Task<Answer> AdminAsync(HttpMethod method, string path, object? body = null) => SendAsync(_anonymous, method, path, body, key: _adminKey);

    private static string Status(Answer answer) => answer.Json!["status"]!.GetValue<string>();

    private static string NextMessage(Answer answer) => answer.Json!["next"]!["message"]!.GetValue<string>();

    private static string NextError(Answer answer) => answer.Json!["next"]!["error"]!.GetValue<string>();

    private static object PolicyBody(string name, string password = Password, bool enabled = false, int? keepLast = 3)
        => new { form = new { name, scopeKind = "repository", scopeName = "default", enabled, keepLast }, password };

    private async Task<int> CreatePolicyAsync(string name, bool enabled = false)
    {
        var created = await AdminAsync(HttpMethod.Post, "/api/ui/admin/retention", PolicyBody(name, enabled: enabled));
        Assert.Equal("Next", Status(created));
        Assert.Equal("AdminRetention/Policy", JsonScreen.Name(created.Json!["next"]!["screen"]!));

        return created.Json["next"]!["routeValues"]!["key"]!.GetValue<int>();
    }

    private static object ReplicationBody(string name, string? apiKey = PeerKey, string peerUrl = "http://peer.invalid:9/", string password = Password)
        => new
        {
            form = new { name, repository = "default", direction = "Push", peerUrl, peerRepository = "default", apiKey, enabled = true, conflict = "TargetWins" },
            password,
        };

    private async Task<int> CreateReplicationAsync(string name)
    {
        var created = await AdminAsync(HttpMethod.Post, "/api/ui/admin/replication", ReplicationBody(name));
        Assert.True(Status(created) == "Next", created.Body);

        return created.Json!["next"]!["routeValues"]!["key"]!.GetValue<int>();
    }

    // ---- who may open a screen ----

    public static TheoryData<string> ReadScreens => new()
    {
        "/api/ui/admin/retention",
        "/api/ui/admin/retention/holds",
        "/api/ui/admin/scanning",
        "/api/ui/admin/scanning/default",
        "/api/ui/admin/replication",
    };

    [Theory]
    [MemberData(nameof(ReadScreens))]
    public async Task AScreenIsAnAdminApiKeyAndNobodyElse(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_alice, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_root, HttpMethod.Get, path)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _adminKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: GlobalKey)).Status);

        // A key without the administration scope, a limited one and a plain user's key.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _pushOnlyAdminKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _limitedAdminKey)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _aliceKey)).Status);
    }

    [Fact]
    public async Task APolicyAndAReplicationAndAProjectScreenFollowTheSameRule()
    {
        var policy = await CreatePolicyAsync("rule");
        var replication = await CreateReplicationAsync("peer");

        foreach (var path in new[] { $"/api/ui/admin/retention/{policy}", $"/api/ui/admin/replication/{replication}" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_anonymous, HttpMethod.Get, path)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_alice, HttpMethod.Get, path)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(_root, HttpMethod.Get, path)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_anonymous, HttpMethod.Get, path, key: _aliceKey)).Status);
        }
    }

    [Fact]
    public async Task AnUnknownItemIsAJson404()
    {
        foreach (var path in new[]
        {
            "/api/ui/admin/retention/9999",
            "/api/ui/admin/replication/9999",
            "/api/ui/admin/scanning/nope",
            "/api/ui/admin/scanning/projects/nope",
        })
        {
            var answer = await AdminAsync(HttpMethod.Get, path);

            Assert.Equal(HttpStatusCode.NotFound, answer.Status);
            Assert.Equal("NotFound", Status(answer));
        }

        foreach (var path in new[]
        {
            "/api/ui/admin/retention/9999/dry-run",
            "/api/ui/admin/retention/9999/run",
            "/api/ui/admin/retention/9999/delete",
            "/api/ui/admin/replication/9999/run",
            "/api/ui/admin/replication/9999/resume",
            "/api/ui/admin/replication/9999/stop",
            "/api/ui/admin/replication/9999/delete",
            "/api/ui/admin/scanning/nope/scan",
            "/api/ui/admin/scanning/nope/policy",
            "/api/ui/admin/scanning/projects/nope/policy",
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Post, path, new { password = Password })).Status);
        }
    }

    // ---- writes ----

    [Fact]
    public async Task ACookieWriteIs401AndAKeyWriteNeedsNoAntiforgeryHeader()
    {
        var refused = await SendAsync(_root, HttpMethod.Post, "/api/ui/admin/retention", PolicyBody("cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Empty(await _app.Services.GetRequiredService<IRetentionAdministration>().ListPoliciesAsync(Token));

        var withKey = await AdminAsync(HttpMethod.Post, "/api/ui/admin/retention", PolicyBody("by-key"));
        Assert.Equal("Next", Status(withKey));
        Assert.Single(await _app.Services.GetRequiredService<IRetentionAdministration>().ListPoliciesAsync(Token));
    }

    [Fact]
    public async Task EveryWriteIsRefusedWith503WhileTheServerIsReadOnly()
    {
        var policy = await CreatePolicyAsync("gated");
        var replication = await CreateReplicationAsync("gated-peer");
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("Copying the database");
        try
        {
            foreach (var (path, body) in new (string, object)[]
            {
                ("/api/ui/admin/retention", PolicyBody("again")),
                ($"/api/ui/admin/retention/{policy}/save", PolicyBody("gated")),
                ($"/api/ui/admin/retention/{policy}/dry-run", new { }),
                ($"/api/ui/admin/retention/{policy}/run", new { password = Password }),
                ($"/api/ui/admin/retention/{policy}/delete", new { password = Password }),
                ("/api/ui/admin/retention/holds/add", new { holdRepository = "default", holdName = "x" }),
                ("/api/ui/admin/retention/holds/remove", new { key = 1, password = Password }),
                ("/api/ui/admin/scanning/default/policy", new { warn = "1", password = Password }),
                ("/api/ui/admin/scanning/default/scan", new { }),
                ("/api/ui/admin/replication", ReplicationBody("again")),
                ($"/api/ui/admin/replication/{replication}/save", ReplicationBody("gated-peer")),
                ($"/api/ui/admin/replication/{replication}/test", ReplicationBody("gated-peer")),
                ($"/api/ui/admin/replication/{replication}/run", new { password = Password }),
                ($"/api/ui/admin/replication/{replication}/resume", new { password = Password }),
                ($"/api/ui/admin/replication/{replication}/stop", new { }),
                ($"/api/ui/admin/replication/{replication}/delete", new { password = Password }),
            })
            {
                var answer = await AdminAsync(HttpMethod.Post, path, body);

                Assert.True(answer.Status == HttpStatusCode.ServiceUnavailable, $"{path} answered {answer.Status}");
            }

            // Reading still works.
            Assert.Equal(HttpStatusCode.OK, (await AdminAsync(HttpMethod.Get, "/api/ui/admin/retention")).Status);
        }
        finally
        {
            gate.Open();
        }
    }

    // ---- retention ----

    [Fact]
    public async Task APolicyIsCreatedWithThePasswordAndTheListOfThePageAndOfTheTwinAgree()
    {
        var wrong = await AdminAsync(HttpMethod.Post, "/api/ui/admin/retention", PolicyBody("first", password: "wrong"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Equal("Invalid", Status(wrong));
        Assert.Contains("confirmation is incorrect", wrong.Json!["model"]!["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(wrong.Json["model"]!["policies"]!.AsArray());

        var refused = await AdminAsync(HttpMethod.Post, "/api/ui/admin/retention", PolicyBody(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.False(string.IsNullOrEmpty(refused.Json!["errors"]![""]![0]!.GetValue<string>()));
        Assert.NotNull(refused.Json["model"]!["repositories"]);

        var key = await CreatePolicyAsync("first");
        var list = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/retention")).Json!["model"]!;
        Assert.Equal("first", list["policies"]![0]!["policy"]!["name"]!.GetValue<string>());
        Assert.Contains("first", await _root.GetPageAsync("/admin/retention"), StringComparison.Ordinal);

        var one = (await AdminAsync(HttpMethod.Get, $"/api/ui/admin/retention/{key}")).Json!["model"]!;
        Assert.Equal(key, one["key"]!.GetValue<int>());
        Assert.Equal(3, one["form"]!["keepLast"]!.GetValue<int>());
        Assert.Equal("default", one["view"]!["repository"]!.GetValue<string>());
        Assert.Contains("first", await _root.GetPageAsync($"/admin/retention/{key}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASavedPolicyADryRunARunAndADeleteAnswerLikeThePages()
    {
        var key = await CreatePolicyAsync("lifecycle");

        var wrong = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/retention/{key}/save", PolicyBody("renamed", password: "wrong"));
        Assert.Equal("Next", Status(wrong));
        Assert.Contains("confirmation is incorrect", NextError(wrong), StringComparison.Ordinal);

        var saved = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/retention/{key}/save", PolicyBody("renamed", keepLast: 5));
        Assert.Equal("Policy renamed was saved.", NextMessage(saved));
        Assert.Equal(5, (await AdminAsync(HttpMethod.Get, $"/api/ui/admin/retention/{key}")).Json!["model"]!["form"]!["keepLast"]!.GetValue<int>());

        var dry = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/retention/{key}/dry-run");
        Assert.Equal("The dry run started; nothing is deleted.", NextMessage(dry));

        // The policy is disabled: a run is refused with the reason; the password is asked first.
        var run = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/retention/{key}/run", new { password = Password });
        Assert.Contains("disabled", NextError(run), StringComparison.Ordinal);
        var runWrong = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/retention/{key}/run", new { password = "wrong" });
        Assert.Contains("confirmation is incorrect", NextError(runWrong), StringComparison.Ordinal);

        var kept = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/retention/{key}/delete", new { password = "wrong" });
        Assert.Contains("confirmation is incorrect", NextError(kept), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(HttpMethod.Get, $"/api/ui/admin/retention/{key}")).Status);

        var deleted = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/retention/{key}/delete", new { password = Password });
        Assert.Equal("AdminRetention/Index", JsonScreen.Name(deleted.Json!["next"]!["screen"]!));
        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Get, $"/api/ui/admin/retention/{key}")).Status);
    }

    [Fact]
    public async Task ALegalHoldIsPlacedWithoutAPasswordListedAndLiftedWithOne()
    {
        var added = await AdminAsync(HttpMethod.Post, "/api/ui/admin/retention/holds/add?repository=default", new { holdRepository = "default", holdName = "Some.Package", holdVersion = "1.0.0", reason = "case 12" });
        Assert.Equal("Next", Status(added));
        Assert.Equal("Some.Package 1.0.0 is on legal hold.", NextMessage(added));

        var bad = await AdminAsync(HttpMethod.Post, "/api/ui/admin/retention/holds/add", new { holdRepository = "nowhere", holdName = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal("nowhere", bad.Json!["model"]!["holdRepository"]!.GetValue<string>());

        var holds = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/retention/holds?repository=default")).Json!["model"]!["holds"]!.AsArray();
        var hold = Assert.Single(holds)!;
        Assert.Equal("case 12", hold["reason"]!.GetValue<string>());
        Assert.Contains("Some.Package", await _root.GetPageAsync("/admin/retention/holds?repository=default"), StringComparison.Ordinal);

        var keyOfHold = hold["key"]!.GetValue<int>();
        var wrong = await AdminAsync(HttpMethod.Post, "/api/ui/admin/retention/holds/remove", new { key = keyOfHold, password = "wrong" });
        Assert.Contains("confirmation is incorrect", NextError(wrong), StringComparison.Ordinal);
        Assert.Single((await AdminAsync(HttpMethod.Get, "/api/ui/admin/retention/holds")).Json!["model"]!["holds"]!.AsArray());

        var lifted = await AdminAsync(HttpMethod.Post, "/api/ui/admin/retention/holds/remove", new { key = keyOfHold, password = Password });
        Assert.Equal("The hold was lifted.", NextMessage(lifted));
        Assert.Empty((await AdminAsync(HttpMethod.Get, "/api/ui/admin/retention/holds")).Json!["model"]!["holds"]!.AsArray());
    }

    // ---- scanning ----

    [Fact]
    public async Task TheOverviewCarriesTheSettingsTheScreenShowsAndNeverTheScannersCredential()
    {
        var overview = await AdminAsync(HttpMethod.Get, "/api/ui/admin/scanning");
        var model = overview.Json!["model"]!;

        Assert.False(model["settings"]!["enabled"]!.GetValue<bool>());
        Assert.True(model["settings"]!["scanOnDeploy"]!.GetValue<bool>());
        Assert.Equal("default", model["repositories"]![0]!["repository"]!["name"]!.GetValue<string>());
        Assert.Contains("default", await _root.GetPageAsync("/admin/scanning"), StringComparison.Ordinal);
        Assert.DoesNotContain(TrivySecret, overview.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("trivyRegistryPassword", overview.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrivySecret, await _root.GetPageAsync("/admin/scanning"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARepositoryScanPolicyIsSavedWithThePasswordAndShownByTheDashboard()
    {
        var wrong = await AdminAsync(HttpMethod.Post, "/api/ui/admin/scanning/default/policy", new { warn = "1", blockDownload = "3", password = "wrong" });
        Assert.Contains("confirmation is incorrect", NextError(wrong), StringComparison.Ordinal);

        var invalid = await AdminAsync(HttpMethod.Post, "/api/ui/admin/scanning/default/policy", new { warn = "9", password = Password });
        Assert.Equal("Next", Status(invalid));
        Assert.NotEmpty(NextError(invalid));

        var saved = await AdminAsync(HttpMethod.Post, "/api/ui/admin/scanning/default/policy", new { warn = "1", blockDownload = "3", blockUnscanned = true, password = Password });
        Assert.Equal("The scanning policy of default was saved.", NextMessage(saved));
        Assert.Equal("AdminScanning/Repository", JsonScreen.Name(saved.Json!["next"]!["screen"]!));

        var dashboard = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/scanning/default?min=1&p=0")).Json!["model"]!;
        Assert.Equal(1, dashboard["own"]!["warnSeverity"]!.GetValue<int>());
        Assert.Equal(3, dashboard["own"]!["blockDownloadSeverity"]!.GetValue<int>());
        Assert.True(dashboard["own"]!["blockUnscanned"]!.GetValue<bool>());
        Assert.Equal(1, dashboard["pageNumber"]!.GetValue<int>());
        Assert.Equal(1, dashboard["minSeverity"]!.GetValue<int>());
        Assert.Contains("Vulnerabilities", await _root.GetPageAsync("/admin/scanning/default"), StringComparison.Ordinal);

        var removed = await AdminAsync(HttpMethod.Post, "/api/ui/admin/scanning/default/policy", new { password = Password });
        Assert.Equal("The scanning policy of default was removed.", NextMessage(removed));
    }

    [Fact]
    public async Task AScanIsRefusedWithTheReasonWhileScanningIsOff()
    {
        var answer = await AdminAsync(HttpMethod.Post, "/api/ui/admin/scanning/default/scan");

        Assert.Equal("Next", Status(answer));
        Assert.Contains("Scanning is not available", NextError(answer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFindingNamesThePackageScreenOfItsVersionAsALinkAndThePageResolvesIt()
    {
        using (var package = TestPackages.Build("Linked.Package", "1.0.0"))
        {
            await _app.AddPackageAsync(package, Token);
        }

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IContext>();
            var repository = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("default", Token))!;
            db.ScanResults!.Add(new ScanResult
            {
                RepositoryKey = repository.Key,
                Name = "linked.package",
                Version = "1.0.0",
                Scanner = "osv",
                ScannedAt = DateTime.UtcNow,
                Attempted = DateTime.UtcNow,
                Highest = 2,
                FindingCount = 1,
                Findings = new List<ScanFinding> { new() { VulnerabilityId = "GHSA-test", Component = "linked.package", ComponentVersion = "1.0.0", Severity = 2 } },
            });
            await db.SaveChangesAsync(Token);
        }

        var dashboard = (await AdminAsync(HttpMethod.Get, "/api/ui/admin/scanning/default")).Json!["model"]!;
        var finding = Assert.Single(dashboard["findings"]!.AsArray())!;
        var link = dashboard["packageLinks"]![finding["key"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture)]!;

        Assert.Equal("Package/Show", JsonScreen.Name(link["screen"]!));
        Assert.Equal("linked.package", link["routeValues"]!["id"]!.GetValue<string>());
        Assert.Equal("1.0.0", link["routeValues"]!["version"]!.GetValue<string>());
        Assert.Contains("/packages/linked.package/1.0.0", await _root.GetPageAsync("/admin/scanning/default"), StringComparison.OrdinalIgnoreCase);
    }

    // ---- replication ----

    [Fact]
    public async Task AReplicationIsCreatedWithThePasswordAndThePeerKeyIsInNoAnswer()
    {
        var wrong = await AdminAsync(HttpMethod.Post, "/api/ui/admin/replication", ReplicationBody("first", password: "wrong"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Equal("Invalid", Status(wrong));
        Assert.DoesNotContain(PeerKey, wrong.Body, StringComparison.Ordinal);

        var refused = await AdminAsync(HttpMethod.Post, "/api/ui/admin/replication", ReplicationBody("first", peerUrl: "not a url"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.DoesNotContain(PeerKey, refused.Body, StringComparison.Ordinal);
        Assert.Null(refused.Json!["model"]!["form"]!["apiKey"]);

        var key = await CreateReplicationAsync("first");
        var config = await AdminAsync(HttpMethod.Get, $"/api/ui/admin/replication/{key}");
        var model = config.Json!["model"]!;

        Assert.True(model["view"]!["hasApiKey"]!.GetValue<bool>());
        Assert.Equal("first", model["view"]!["config"]!["name"]!.GetValue<string>());
        Assert.Equal("http://peer.invalid:9/", model["form"]!["peerUrl"]!.GetValue<string>());
        Assert.DoesNotContain(PeerKey, config.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedApiKey", config.Body, StringComparison.OrdinalIgnoreCase);

        var list = await AdminAsync(HttpMethod.Get, "/api/ui/admin/replication");
        Assert.Equal("first", list.Json!["model"]!["replications"]![0]!["config"]!["name"]!.GetValue<string>());
        Assert.DoesNotContain("protectedApiKey", list.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first", await _root.GetPageAsync("/admin/replication"), StringComparison.Ordinal);
        Assert.DoesNotContain(PeerKey, await _root.GetPageAsync($"/admin/replication/{key}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATestWithTheWrongPasswordAnswers400AndEchoesNoKeyAndASaveKeepsTheStoredOne()
    {
        var key = await CreateReplicationAsync("tested");

        var wrong = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/replication/{key}/test", ReplicationBody("tested", apiKey: "typed-but-never-echoed", password: "wrong"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Contains("nothing was tested", wrong.Json!["errors"]![""]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain("typed-but-never-echoed", wrong.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(PeerKey, wrong.Body, StringComparison.Ordinal);

        // A save with no key typed keeps the stored one.
        var saved = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/replication/{key}/save", ReplicationBody("tested", apiKey: null));
        Assert.Equal("Replication tested was saved.", NextMessage(saved));
        Assert.True((await AdminAsync(HttpMethod.Get, $"/api/ui/admin/replication/{key}")).Json!["model"]!["view"]!["hasApiKey"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ARunOfADisabledReplicationAStopWithNothingRunningAndADeleteAnswerLikeThePage()
    {
        var key = await CreateReplicationAsync("running");

        var stop = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/replication/{key}/stop");
        Assert.Contains("No run of this replication", NextError(stop), StringComparison.Ordinal);

        var wrong = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/replication/{key}/delete", new { password = "wrong" });
        Assert.Contains("confirmation is incorrect", NextError(wrong), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(HttpMethod.Get, $"/api/ui/admin/replication/{key}")).Status);

        var deleted = await AdminAsync(HttpMethod.Post, $"/api/ui/admin/replication/{key}/delete", new { password = Password });
        Assert.Equal("AdminReplication/Index", JsonScreen.Name(deleted.Json!["next"]!["screen"]!));
        Assert.Equal(HttpStatusCode.NotFound, (await AdminAsync(HttpMethod.Get, $"/api/ui/admin/replication/{key}")).Status);
    }
}
