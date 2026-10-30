using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The view of a screen and its JSON twin (<c>/api/ui</c>) say the same thing: every <c>[UiApi]</c> action serves a screen (and handler) that a view action
/// has; every screen that is not split yet is on the pending list; and, for each screen already split, the model the JSON carries is what the
/// page shows, with corresponding statuses (404 to 404, sign-in redirect to 401). A track adds its screens to <see cref="TheSplitScreensShowTheSameInJsonAndInTheView"/>-style
/// cases below as it moves them.
/// </summary>
public class UiApiParityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BaGetUiApi", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = new();

    public UiApiParityTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
        try { Directory.Delete(_directory, true); } catch (IOException) { }
    }

    private BaGetApplication Start(Dictionary<string, string>? settings = null)
    {
        var app = new BaGetApplication(_output, configOverrides: settings);
        _disposables.Add(app);

        return app;
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);

        return path;
    }

    // ---- structure ----

    private static List<ControllerActionDescriptor> Actions(BaGetApplication app)
        => app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToList();

    [Fact]
    public void EveryUiApiActionServesAScreenAndHandlerThatAViewActionHas()
    {
        using var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        var actions = Actions(app);

        var twins = actions
            .Select(a => (Action: a, Api: a.MethodInfo.GetCustomAttribute<UiApiAttribute>()))
            .Where(p => p.Api != null)
            .ToList();
        Assert.NotEmpty(twins);

        // (screen, handler) pairs of the view actions.
        var views = actions
            .Where(a => ScreenEndpoints.ScreenOf(a) != null)
            .Select(a => (Screen: ScreenEndpoints.ScreenOf(a)!.ToString(), Handler: a.MethodInfo.GetCustomAttribute<PageHandlerAttribute>()?.Name))
            .ToHashSet();

        var missing = twins.Where(p => !views.Contains((p.Api!.Screen.ToString(), p.Api.Handler))).Select(p => $"{p.Action.DisplayName} -> {p.Api!.Screen} handler={p.Api.Handler}").ToList();

        Assert.True(missing.Count == 0, "UI API actions without a view action of that screen and handler:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void AUiApiActionIsNeverAScreenAndLivesUnderApiUi()
    {
        using var app = new BaGetApplication(_output);

        foreach (var action in Actions(app).Where(a => a.MethodInfo.GetCustomAttribute<UiApiAttribute>() != null))
        {
            // Not a view controller: the route snapshot of the screens (ui-routes.txt) must not see the API.
            Assert.False(ScreenEndpoints.IsScreen(action));
            Assert.StartsWith("api/ui/", action.AttributeRouteInfo!.Template!, StringComparison.Ordinal);
            // [ApiController] would answer 400 by itself over HTTP but not in process.
            Assert.DoesNotContain(action.ControllerTypeInfo.GetCustomAttributes(inherit: true), a => a.GetType().Name == "ApiControllerAttribute");
        }
    }

    [Fact]
    public void EveryScreenHasAUiApiTwin()
    {
        using var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        var actions = Actions(app);

        var served = actions.Select(a => a.MethodInfo.GetCustomAttribute<UiApiAttribute>()?.Screen.ToString()).Where(s => s != null).ToHashSet();
        var screens = actions
            .Select(a => ScreenEndpoints.ScreenOf(a)?.ToString())
            .Where(s => s != null)
            .Distinct()
            .Where(s => !served.Contains(s!))
            .ToList();

        Assert.True(screens.Count == 0, "Screens without a [UiApi] twin:" + Environment.NewLine + string.Join(Environment.NewLine, screens));
    }

    // Steps with no JSON twin because they only redirect the browser to the external sign-in provider and back (the challenge and its callback, which
    // set the session cookie; the same for signing in again to re-authenticate and for linking a provider): (screen as Controller/Action, handler, method). Nothing else may be missing; a default POST that only re-renders its screen
    // ([ImplicitPageHandler]) is no operation and needs no twin.
    private static readonly HashSet<(string Screen, string? Handler, string Method)> BrowserOnly = new()
    {
        ("Account/Login", "External", "POST"),
        ("Account/Login", "Callback", "GET"),
        ("Account/Reauthenticate", null, "POST"),
        ("Account/Reauthenticate", "Callback", "GET"),

        // Linking a provider to the signed-in account: the redirect to the provider and back (unlinking has a twin).
        ("AccountSettings/Settings", "LinkExternal", "POST"),
        ("AccountSettings/Settings", "LinkCallback", "GET"),
    };

    // An action with no HTTP method constraint answers any method: demand a twin for GET and POST, never skip it.
    private static string[] MethodsOf(ControllerActionDescriptor action)
    {
        var methods = action.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).Distinct().ToArray();

        return methods is { Length: > 0 } ? methods : new[] { "GET", "POST" };
    }

    [Fact]
    public void EveryScreenAndHandlerOfAViewActionHasAUiApiTwinOfTheSameMethodExceptTheBrowserOnlySteps()
    {
        using var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        var actions = Actions(app);

        var twins = actions
            .Where(a => a.MethodInfo.GetCustomAttribute<UiApiAttribute>() != null)
            .SelectMany(a => MethodsOf(a).Select(m => (a.MethodInfo.GetCustomAttribute<UiApiAttribute>()!.Screen.ToString(), a.MethodInfo.GetCustomAttribute<UiApiAttribute>()!.Handler, Method: m)))
            .ToHashSet();

        var missing = new List<string>();
        var seen = new HashSet<(string, string?, string)>();
        foreach (var view in actions.Where(a => ScreenEndpoints.IsScreen(a) && !a.EndpointMetadata.OfType<ImplicitPageHandlerAttribute>().Any()))
        {
            var screen = ScreenEndpoints.ScreenOf(view)!.ToString();
            var handler = view.MethodInfo.GetCustomAttribute<PageHandlerAttribute>()?.Name;
            foreach (var method in MethodsOf(view).Where(m => m is "GET" or "POST"))
            {
                var key = (screen, handler, method);
                if (!seen.Add(key) || twins.Contains(key) || BrowserOnly.Contains(key)) continue;

                missing.Add($"{method} {screen} handler={handler} ({view.DisplayName})");
            }
        }

        // The allow-list names real view actions (it cannot rot).
        foreach (var entry in BrowserOnly)
        {
            Assert.Contains(seen, k => k.Equals(entry));
        }

        Assert.True(missing.Count == 0, "View actions (screen, handler, method) without a [UiApi] twin and not on the browser-only list:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    // ---- pilot: legal pages ----

    [Fact]
    public async Task ALegalPageAndItsTwinCarryTheSameRenderedText()
    {
        using var app = Start(new Dictionary<string, string>
        {
            { "Site:TermsOfUsePath", WriteFile("terms.md", "## Our terms\n\nBe **kind**.") },
            { "Site:PrivacyPolicyPath", WriteFile("privacy.md", "# Privacy\n\n<script>alert(1)</script>") },
        });
        using var client = app.CreateClient();
        var token = TestContext.Current.CancellationToken;

        var page = await client.GetStringAsync("/terms", token);
        var json = JsonNode.Parse(await client.GetStringAsync("/api/ui/terms", token))!;

        Assert.Equal("Ok", json["status"]!.GetValue<string>());
        Assert.Equal("Terms of Use", json["model"]!["title"]!.GetValue<string>());
        var html = json["model"]!["contentHtml"]!.GetValue<string>();
        Assert.Contains("<strong>kind</strong>", html);
        Assert.Contains(html.Trim(), page);

        // The privacy policy is rendered safely in both.
        var privacy = JsonNode.Parse(await client.GetStringAsync("/api/ui/privacy", token))!["model"]!["contentHtml"]!.GetValue<string>();
        Assert.DoesNotContain("<script>", privacy, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", await client.GetStringAsync("/privacy", token), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("terms")]
    [InlineData("privacy")]
    public async Task ALegalPageWithNoFileIs404InBothWays(string name)
    {
        using var app = Start();
        using var client = app.CreateClient();
        var token = TestContext.Current.CancellationToken;

        using var page = await client.GetAsync("/" + name, token);
        using var api = await client.GetAsync("/api/ui/" + name, token);

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Equal("NotFound", JsonNode.Parse(await api.Content.ReadAsStringAsync(token))!["status"]!.GetValue<string>());
    }

    // ---- pilot: statistics ----

    [Fact]
    public async Task TheStatisticsPageAndItsTwinCountTheSame()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var token = TestContext.Current.CancellationToken;
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await app.AddPackageAsync(package, token);
        }

        var page = await client.GetStringAsync("/stats", token);
        var json = JsonNode.Parse(await client.GetStringAsync("/api/ui/stats", token))!;
        var statistics = json["model"]!["statistics"]!;

        Assert.Equal(1, statistics["packageIds"]!.GetValue<int>());
        Assert.Equal(1, statistics["packageVersions"]!.GetValue<int>());
        Assert.Contains("<h2 id=\"stat-ids\">1</h2>", page);
        Assert.Contains("<h2 id=\"stat-versions\">1</h2>", page);
        Assert.Equal("TestData", statistics["topPackages"]![0]!["id"]!.GetValue<string>());
        Assert.Contains("TestData", page);
    }

    [Fact]
    public async Task TheStatisticsOfAnotherRepositoryAreTheSameScreenUnderItsTwinRoute()
    {
        using var app = Start();
        await app.CreateRepositoryAsync("second", TestContext.Current.CancellationToken);
        using var client = app.CreateClient();
        var token = TestContext.Current.CancellationToken;

        using var page = await client.GetAsync("/r/second/stats", token);
        using var api = await client.GetAsync("/api/ui/r/second/stats", token);
        using var missingPage = await client.GetAsync("/r/nope/stats", token);
        using var missingApi = await client.GetAsync("/api/ui/r/nope/stats", token);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingPage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingApi.StatusCode);
    }

    [Fact]
    public async Task APrivateRepositoryIsASignInForAnAnonymousVisitorOnTheTwinAndOnThePage()
    {
        using var app = Start(new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        var token = TestContext.Current.CancellationToken;
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BaGet.Core.IRepositoryService>()
                .CreateAsync("closed", BaGet.Core.RepositoryFormat.NuGet, BaGet.Core.RepositoryKind.Local, new Dictionary<string, string?>(), anonymousRead: false, token);
        }

        using var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var page = await client.GetAsync("/r/closed/stats", token);
        using var api = await client.GetAsync("/api/ui/r/closed/stats", token);

        // The page redirects to the sign-in page, the twin answers 401: the same decision in each programming model.
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    // ---- browse: front page, list, package, report abuse ----

    [Fact]
    public async Task TheFrontPageAndTheListShowWhatTheirTwinsCarry()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var token = TestContext.Current.CancellationToken;
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await app.AddPackageAsync(package, token);
        }

        var homePage = await client.GetStringAsync("/", token);
        var home = JsonNode.Parse(await client.GetStringAsync("/api/ui/home", token))!["model"]!;
        var listPage = await client.GetStringAsync("/packages", token);
        var list = JsonNode.Parse(await client.GetStringAsync("/api/ui/packages", token))!["model"]!;
        var searchPage = await client.GetStringAsync("/?q=TestData", token);
        var search = JsonNode.Parse(await client.GetStringAsync("/api/ui/home?q=TestData", token))!["model"]!;

        Assert.True(home["isHome"]!.GetValue<bool>());
        Assert.Contains("id=\"home-packages\">" + home["totals"]!["packageIds"]!.GetValue<int>(), homePage);

        // /packages is the list even without a search, and so is the root with a q parameter.
        Assert.False(list["isHome"]!.GetValue<bool>());
        Assert.False(search["isHome"]!.GetValue<bool>());
        Assert.Equal("TestData", list["packages"]![0]!["id"]!.GetValue<string>());
        Assert.Contains("TestData", listPage);
        Assert.Contains("TestData", searchPage);
        Assert.DoesNotContain("id=\"home-packages\"", listPage);
    }

    [Fact]
    public async Task TheListStatusesMatchBetweenThePageAndTheTwin()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var token = TestContext.Current.CancellationToken;

        foreach (var query in new[] { "p=0", "sortBy=99", "p=abc" })
        {
            using var page = await client.GetAsync("/packages?" + query, token);
            using var api = await client.GetAsync("/api/ui/packages?" + query, token);

            Assert.Equal(HttpStatusCode.BadRequest, page.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, api.StatusCode);
        }
    }

    [Fact]
    public async Task ThePackagePageAndItsTwinShowTheSameVersionAndVersions()
    {
        using var app = Start();
        await app.CreateRepositoryAsync("second", TestContext.Current.CancellationToken);
        using var client = app.CreateClient();
        var token = TestContext.Current.CancellationToken;
        using (var package = TestResources.GetResourceStream(TestResources.Package))
        {
            await app.AddPackageAsync(package, token);
        }

        var page = await client.GetStringAsync("/packages/TestData/1.2.3", token);
        var json = JsonNode.Parse(await client.GetStringAsync("/api/ui/packages/TestData/1.2.3", token))!["model"]!;

        Assert.True(json["found"]!.GetValue<bool>());
        Assert.Equal("1.2.3", json["package"]!["version"]!.GetValue<string>());
        Assert.Equal("1.2.3", json["versions"]![0]!["version"]!.GetValue<string>());
        Assert.Contains("TestData", page);
        Assert.Contains("1.2.3", page);
        Assert.NotEmpty(json["installTabs"]!.AsArray());

        // An unknown package is a page that says so (200), and the same in JSON; an unknown repository is a 404 in both.
        using var missingPage = await client.GetAsync("/packages/NoSuch", token);
        using var missingApi = await client.GetAsync("/api/ui/packages/NoSuch", token);
        using var noRepositoryPage = await client.GetAsync("/r/nope/packages/TestData", token);
        using var noRepositoryApi = await client.GetAsync("/api/ui/r/nope/packages/TestData", token);
        using var secondApi = await client.GetAsync("/api/ui/r/second/packages/TestData", token);

        Assert.Equal(HttpStatusCode.OK, missingPage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, missingApi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noRepositoryPage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noRepositoryApi.StatusCode);
        Assert.False(JsonNode.Parse(await secondApi.Content.ReadAsStringAsync(token))!["model"]!["found"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AReportThroughThePageAndAReportThroughTheTwinRecordTheSameState()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var token = TestContext.Current.CancellationToken;

        var form = await client.GetStringAsync("/packages/TestData/1.2.3/report-abuse", token);
        Assert.Contains("name=\"Reason\"", form);

        // The twin takes the form as JSON, anonymously and without any token: /api/ui has no ambient credential to forge.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui/packages/TestData/1.2.3/report-abuse")
        {
            Content = new StringContent("{\"reason\":\"Other\",\"details\":\"This is bad.\"}", System.Text.Encoding.UTF8, "application/json"),
        };
        using var posted = await client.SendAsync(request, token);
        var json = JsonNode.Parse(await posted.Content.ReadAsStringAsync(token))!;

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.True(json["model"]!["submitted"]!.GetValue<bool>());
        using var scope = app.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<BaGet.Core.IContext>().AbuseReports!.ToList();
        Assert.Single(reports);
        Assert.Equal("TestData", reports[0].PackageId);
        Assert.Equal("1.2.3", reports[0].PackageVersion);
    }
}
