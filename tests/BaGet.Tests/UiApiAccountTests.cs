using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Mvc.UI;
using BaGet.Web.V3.OpenApi;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The account and package-ownership screens exist in the web UI only: their JSON twins (<c>/api/ui/account/...</c>, the manage pages, ...) answer 403
/// "use the web screen" to everybody over HTTP, because over HTTP only API keys authenticate and an API key never changes credentials or ownership.
/// The rules behind them (lockout, approval, return-URL rule, passwords asked again, two-factor) are the use cases the web screens call in process, covered by
/// the page tests (<c>AccountSettingsTests</c>, <c>AccountApprovalTests</c>, ...). The public profile is not screen-only.
/// </summary>
public class UiApiAccountTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public UiApiAccountTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private BaGetApplication Start(Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { { "Accounts:Enabled", "true" } };
        foreach (var pair in extra ?? new Dictionary<string, string>()) settings[pair.Key] = pair.Value;

        var app = new BaGetApplication(_output, configOverrides: settings);
        _disposables.Add(app);

        return app;
    }

    private Browser NewBrowser(BaGetApplication app)
    {
        var browser = new Browser(app);
        _disposables.Add(browser);

        return browser;
    }

    private sealed record Answer(HttpStatusCode Status, JsonNode? Json, string Body, HttpResponseHeaders Headers);

    private static async Task<Answer> SendAsync(Browser browser, HttpMethod method, string path, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (method != HttpMethod.Get) request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await browser.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync(Token);

        return new Answer(response.StatusCode, text.Length > 0 && text[0] == '{' ? JsonNode.Parse(text) : null, text, response.Headers);
    }

    private static Task<Answer> GetAsync(Browser browser, string path, string? key = null) => SendAsync(browser, HttpMethod.Get, path, key);

    private static async Task<string> KeyForAsync(BaGetApplication app, string user, params string[] scopes)
    {
        using var scope = app.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync((await FindAsync(app, user))!.Id, "test", scopes, "*", null, null, Token);
    }

    // The address of every screen-only twin action, as (method, path) with the route parameters filled in.
    private static List<(string Method, string Path, bool OpenToAnonymous)> ScreenOnlyTwins(BaGetApplication app)
    {
        var twins = new List<(string, string, bool)>();

        foreach (var action in app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            if (action.MethodInfo.GetCustomAttributes(typeof(UiApiAttribute), true).Length == 0 || !ApiDocuments.IsScreenOnly(action.ControllerTypeInfo)) continue;

            var path = "/" + Regex.Replace(action.AttributeRouteInfo!.Template!, @"\{(\*)?repo[^}]*\}", "default");
            path = Regex.Replace(path, @"\{[^}]+\}", "1");
            var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).Distinct().ToArray() ?? new[] { "GET" };
            foreach (var method in methods) twins.Add((method, path, action.MethodInfo.GetCustomAttributes(typeof(BaGet.Frontend.Mvc.Attributes.AnonymousSessionEndpointAttribute), true).Length > 0));
        }

        return twins;
    }

    [Fact]
    public async Task EveryScreenOnlyTwinAnswers403ToAnySignedInBrowserAnonymousVisitorAndKeyHolder()
    {
        // Mail is configured so that the mail screens are not hidden (404) before the rule applies.
        var app = Start(new Dictionary<string, string>
        {
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
            { "Site:PublicUrl", "https://baget.example.com" },
        });
        await CreateUserAsync(app, "alice", email: "alice@example.com");
        await CreateUserAsync(app, "root", administrator: true, email: "root@example.com");
        var alice = NewBrowser(app);
        await SignInAsync(alice, "alice");
        var anonymous = NewBrowser(app);
        var aliceKey = await KeyForAsync(app, "alice", ApiKeyScopes.All);
        var adminKey = await KeyForAsync(app, "root", ApiKeyScopes.Admin);

        var twins = ScreenOnlyTwins(app);
        Assert.True(twins.Count > 40, "the screen-only twins were not found: " + twins.Count);
        Assert.Contains(twins, t => t.Path == "/api/ui/account/login");
        Assert.Contains(twins, t => t.Path.StartsWith("/api/ui/packages/1/manage", StringComparison.Ordinal));

        var failures = new List<string>();
        foreach (var (method, path, open) in twins)
        {
            foreach (var (who, browser, key) in new[] { ("cookie", alice, (string?)null), ("anonymous", anonymous, null), ("user key", anonymous, aliceKey), ("admin key", anonymous, adminKey) })
            {
                // Registration, confirm email, forgot and reset password are open to a caller without a session (and without a key): see UiApiAnonymousFlowTests.
                if (open && key == null) continue;

                var answer = await SendAsync(browser, new HttpMethod(method), path, key);
                if (answer.Status != HttpStatusCode.Forbidden || answer.Json?["status"]?.GetValue<string>() != "Forbidden"
                    || answer.Json["errors"]?[""]?[0]?.GetValue<string>().Contains("web screen", StringComparison.Ordinal) != true)
                {
                    failures.Add($"{who}: {method} {path} answered {(int)answer.Status} {answer.Body}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task EveryAccountScreenIs404WhileAccountsAreOff()
    {
        using var app = new BaGetApplication(_output);
        using var browser = new Browser(app);

        foreach (var path in new[] { "login", "register", "settings", "apikeys", "organizations" })
        {
            var answer = await GetAsync(browser, "/api/ui/account/" + path);

            Assert.True(answer.Status == HttpStatusCode.NotFound, $"{path} answered {answer.Status}");
            Assert.Equal("NotFound", answer.Json!["status"]!.GetValue<string>());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(browser, "/api/ui/profiles/alice")).Status);
    }

    [Fact]
    public async Task TheMailScreensAre404WithoutMail()
    {
        var app = Start();
        var browser = NewBrowser(app);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(browser, "/api/ui/account/forgotpassword")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(browser, "/api/ui/account/resetpassword")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(browser, "/api/ui/account/confirmemail")).Status);
    }

    [Fact]
    public async Task SigningInThroughThePageStillWorksNowThatTheTwinIsScreenOnly()
    {
        // The page calls the twin in process: no HTTP filter, the cookie session is set as before.
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);

        await SignInAsync(browser, "alice");

        Assert.Contains("Account settings", await browser.GetPageAsync("/account/settings"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AProfileIsPublicDataAKeyMayReadTheCookieIsIgnoredAndAnUnknownNameIs404InBothWays()
    {
        var app = Start();
        await CreateUserAsync(app, "alice");
        var browser = NewBrowser(app);
        await SignInAsync(browser, "alice");
        await PushAsync(app, "alice", "Alice.Lib");
        await app.CreateRepositoryAsync("second", Token);
        var key = await KeyForAsync(app, "alice", ApiKeyScopes.Push);
        var anonymous = NewBrowser(app);

        var viaCookie = await GetAsync(browser, "/api/ui/profiles/alice");
        var viaKey = await GetAsync(anonymous, "/api/ui/profiles/alice", key: key);
        var otherRepository = await GetAsync(anonymous, "/api/ui/r/second/profiles/alice");
        var unknownRepository = await GetAsync(anonymous, "/api/ui/r/nope/profiles/alice");
        var unknown = await GetAsync(anonymous, "/api/ui/profiles/nobody");
        using var unknownPage = await anonymous.GetAsync("/profiles/nobody");

        // The cookie is ignored: the browser of alice reads her profile as an anonymous visitor does; her key reads it as herself.
        Assert.False(viaCookie.Json!["model"]!["isCurrentUser"]!.GetValue<bool>());
        Assert.True(viaKey.Json!["model"]!["isCurrentUser"]!.GetValue<bool>());
        Assert.Equal("Alice.Lib", viaKey.Json["model"]!["packages"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, otherRepository.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknownRepository.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknownPage.StatusCode);
        Assert.DoesNotContain(key, viaKey.Body, StringComparison.Ordinal);
    }
}
