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
/// The retention pages (P6): policies, a policy's dry run, run and report, and legal holds. Administrators only, the password where
/// data is deleted or a hold lifted, 503 while the server is read-only, every change audited, linked from the admin menu.
/// </summary>
public class RetentionPagesTests : IDisposable
{
    private readonly ManualSystemTime _clock = new();
    private readonly BaGetApplication _app;
    private readonly Browser _admin;

    public RetentionPagesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Accounts:Enabled", "true" },
                { "ApiKey", "" },
            },
            configureServices: services => services.AddSingleton<SystemTime>(_clock));
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task PushAsync(string id, string version)
    {
        using var package = TestPackages.Build(id, version);
        await _app.AddPackageAsync(package, Token);
    }

    private static Dictionary<string, string> PolicyFields(string name, string repository = "default", string password = Password, string? keep = "1", string? older = null)
    {
        var fields = new Dictionary<string, string>
        {
            { "Form.Name", name },
            { "Form.ScopeKind", "repository" },
            { "Form.ScopeName", repository },
            { "Form.Enabled", "true" },
            { "password", password },
        };
        if (keep != null) fields["Form.KeepLast"] = keep;
        if (older != null) fields["Form.OlderThanDays"] = older;

        return fields;
    }

    private async Task<int> CreatePolicyAsync(string name, string? keep = "1", string? older = null)
    {
        using var response = await _admin.PostAsync("admin/retention", PolicyFields(name, keep: keep, older: older));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return int.Parse(response.Headers.Location!.ToString().Split('/').Last(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private Task<HttpResponseMessage> PostPolicyAsync(int key, string handler, Dictionary<string, string>? fields = null)
        => _admin.PostAsync($"admin/retention/{key}?handler={handler}", fields ?? new Dictionary<string, string>(), tokenPage: $"admin/retention/{key}");

    private async Task<string[]> VersionsAsync()
    {
        using var scope = _app.Services.CreateScope();
        var packages = await scope.ServiceProvider.GetRequiredService<IPackageDatabase>().FindAsync("Page.Lib", includeUnlisted: true, Token);

        return packages.Select(p => p.NormalizedVersionString!).OrderBy(v => v, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task OnlyAdministratorsSeeThePagesAndTheMenuLinksToThem()
    {
        await CreateUserAsync(_app, "alice");
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");
        using var anonymous = new Browser(_app);

        foreach (var path in new[] { "admin/retention", "admin/retention/holds", "admin/retention/1" })
        {
            using var asUser = await alice.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, asUser.StatusCode);

            using var asAnonymous = await anonymous.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.OK, asAnonymous.StatusCode);
        }

        using var post = await alice.RawPostAsync("admin/retention", PolicyFields("sneaky"));
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.Contains("href=\"/admin/retention\"", await _admin.GetPageAsync("admin/retention"));
    }

    [Fact]
    public async Task APolicyIsCreatedWithThePasswordAndListedWithItsRules()
    {
        var wrong = await _admin.PostAsync("admin/retention", PolicyFields("nope", password: "wrong"));
        var wrongHtml = await wrong.Content.ReadAsStringAsync(Token);
        Assert.Contains("The confirmation is incorrect", wrongHtml);

        var key = await CreatePolicyAsync("keep-one", keep: "3", older: "90");

        var list = await _admin.GetPageAsync("admin/retention");
        Assert.Contains("keep-one", list);
        Assert.DoesNotContain("nope", list);
        Assert.Contains("keep the newest 3, older than 90 d", list);

        var detail = await _admin.GetPageAsync($"admin/retention/{key}");
        Assert.Contains("Dry run", detail);
        Assert.Contains("Run now", detail);

        var audit = await _admin.GetPageAsync("admin/audit");
        Assert.Contains("retention.policy.create", audit);
    }

    [Fact]
    public async Task APolicyThatCannotBeRightShowsTheMessageAndStaysOnTheForm()
    {
        using var response = await _admin.PostAsync("admin/retention", PolicyFields("empty", keep: null));
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose at least one rule.", html);
    }

    [Fact]
    public async Task TheDryRunShowsTheExactListAndDeletesNothingThenRunNowDeletesItAfterThePassword()
    {
        await PushAsync("Page.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(1));
        await PushAsync("Page.Lib", "2.0.0");
        var key = await CreatePolicyAsync("page-keep", keep: "1");
        var jobs = _app.Services.GetRequiredService<RetentionJobs>();

        using (var dry = await PostPolicyAsync(key, "DryRun"))
        {
            Assert.Equal(HttpStatusCode.Redirect, dry.StatusCode);
        }

        await jobs.WaitAsync(key);
        var dryPage = await _admin.GetPageAsync($"admin/retention/{key}");
        Assert.Contains("Dry run, nothing deleted. Would delete 1 version(s)", dryPage);
        Assert.Contains("data-outcome=\"Planned\"", dryPage);
        Assert.Contains("Page.Lib", dryPage);
        Assert.Equal(new[] { "1.0.0", "2.0.0" }, await VersionsAsync());

        using (var refused = await PostPolicyAsync(key, "Run", new Dictionary<string, string> { { "password", "wrong" } }))
        {
            Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        }

        Assert.Equal(new[] { "1.0.0", "2.0.0" }, await VersionsAsync());

        using (var run = await PostPolicyAsync(key, "Run", new Dictionary<string, string> { { "password", Password } }))
        {
            Assert.Equal(HttpStatusCode.Redirect, run.StatusCode);
        }

        await jobs.WaitAsync(key);
        var runPage = await _admin.GetPageAsync($"admin/retention/{key}");
        Assert.Contains("Deleted 1 version(s)", runPage);
        Assert.Contains("data-outcome=\"Deleted\"", runPage);
        Assert.Equal(new[] { "2.0.0" }, await VersionsAsync());
    }

    [Fact]
    public async Task APolicyIsEditedAndDeletedWithThePassword()
    {
        var key = await CreatePolicyAsync("edit-me", keep: "5");

        using (var save = await PostPolicyAsync(key, "Save", PolicyFields("edit-me", keep: "2")))
        {
            Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        }

        Assert.Contains("keep the newest 2", await _admin.GetPageAsync("admin/retention"));

        using (var wrong = await PostPolicyAsync(key, "Delete", new Dictionary<string, string> { { "password", "wrong" } }))
        {
            Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        }

        Assert.Contains("edit-me", await _admin.GetPageAsync("admin/retention"));

        using (var delete = await PostPolicyAsync(key, "Delete", new Dictionary<string, string> { { "password", Password } }))
        {
            Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        }

        Assert.DoesNotContain("edit-me", await _admin.GetPageAsync("admin/retention"));
        using var gone = await _admin.GetAsync($"admin/retention/{key}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task ALegalHoldIsPlacedFromThePageKeepsTheVersionAndNeedsThePasswordToLift()
    {
        await PushAsync("Page.Lib", "1.0.0");
        _clock.Advance(TimeSpan.FromDays(30));

        using (var add = await _admin.PostAsync("admin/retention/holds?handler=Add", new Dictionary<string, string>
        {
            { "HoldRepository", "default" },
            { "HoldName", "page.lib" },
            { "HoldVersion", "1.0" },
            { "Reason", "case 7" },
        }, tokenPage: "admin/retention/holds"))
        {
            Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        }

        var holds = await _admin.GetPageAsync("admin/retention/holds");
        Assert.Contains("data-name=\"page.lib\"", holds);
        Assert.Contains("1.0.0", holds);
        Assert.Contains("case 7", holds);

        var key = await CreatePolicyAsync("hold-test", keep: null, older: "1");
        using (var dry = await PostPolicyAsync(key, "DryRun")) { }

        await _app.Services.GetRequiredService<RetentionJobs>().WaitAsync(key);
        var page = await _admin.GetPageAsync($"admin/retention/{key}");
        Assert.Contains("data-outcome=\"Held\"", page);
        Assert.Contains("1 kept by legal hold", page);

        var holdKey = int.Parse(System.Text.RegularExpressions.Regex.Match(holds, "data-hold=\"(\\d+)\"").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        using (var wrong = await _admin.PostAsync("admin/retention/holds?handler=Remove", new Dictionary<string, string> { { "key", holdKey.ToString(System.Globalization.CultureInfo.InvariantCulture) }, { "password", "wrong" } }, tokenPage: "admin/retention/holds"))
        {
            Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        }

        Assert.Contains("data-hold=", await _admin.GetPageAsync("admin/retention/holds"));

        using (var lifted = await _admin.PostAsync("admin/retention/holds?handler=Remove", new Dictionary<string, string> { { "key", holdKey.ToString(System.Globalization.CultureInfo.InvariantCulture) }, { "password", Password } }, tokenPage: "admin/retention/holds"))
        {
            Assert.Equal(HttpStatusCode.Redirect, lifted.StatusCode);
        }

        Assert.Contains("No hold is placed.", await _admin.GetPageAsync("admin/retention/holds"));
        var audit = await _admin.GetPageAsync("admin/audit");
        Assert.Contains("retention.hold.add", audit);
        Assert.Contains("retention.hold.remove", audit);
    }

    [Fact]
    public async Task AHoldForAnUnknownRepositoryShowsTheMessage()
    {
        using var response = await _admin.PostAsync("admin/retention/holds?handler=Add", new Dictionary<string, string>
        {
            { "HoldRepository", "nope" },
            { "HoldName", "x" },
        }, tokenPage: "admin/retention/holds");
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("There is no repository", html);
    }

    [Fact]
    public async Task EveryWriteIsRefusedWith503WhileTheServerIsReadOnly()
    {
        var key = await CreatePolicyAsync("gated", keep: "1");
        var gate = _app.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("copy");

        try
        {
            using var create = await _admin.PostAsync("admin/retention", PolicyFields("blocked"), tokenPage: "admin/retention");
            using var run = await PostPolicyAsync(key, "Run", new Dictionary<string, string> { { "password", Password } });
            using var hold = await _admin.PostAsync("admin/retention/holds?handler=Add", new Dictionary<string, string> { { "HoldRepository", "default" }, { "HoldName", "x" } }, tokenPage: "admin/retention/holds");
            using var read = await _admin.GetAsync("admin/retention");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, run.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, hold.StatusCode);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }
        finally
        {
            gate.Open();
        }
    }

    [Fact]
    public async Task ARunOfADisabledPolicyFromThePageSaysWhy()
    {
        using var create = await _admin.PostAsync("admin/retention", new Dictionary<string, string>
        {
            { "Form.Name", "off" },
            { "Form.ScopeKind", "repository" },
            { "Form.ScopeName", "default" },
            { "Form.KeepLast", "1" },
            { "password", Password },
        });
        var key = int.Parse(create.Headers.Location!.ToString().Split('/').Last(), System.Globalization.CultureInfo.InvariantCulture);

        using var run = await PostPolicyAsync(key, "Run", new Dictionary<string, string> { { "password", Password } });
        var page = await _admin.GetPageAsync($"admin/retention/{key}");

        Assert.Equal(HttpStatusCode.Redirect, run.StatusCode);
        Assert.Contains("The policy is disabled", page);
    }

    [Fact]
    public async Task AFlashMessageIsShownOnceAfterTheRedirect()
    {
        var key = await CreatePolicyAsync("once");

        using var first = await _admin.GetAsync($"admin/retention/{key}");
        using var second = await _admin.GetAsync($"admin/retention/{key}");

        Assert.Contains("retention-message", await first.Content.ReadAsStringAsync(Token));
        Assert.DoesNotContain("retention-message", await second.Content.ReadAsStringAsync(Token));
    }
}
