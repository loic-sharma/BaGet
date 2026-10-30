using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Identity.Application.Services.Sessions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Core;

namespace BaGet.Tests;

/// <summary>
/// How the host serves a single-page application: CORS (<c>Cors:AllowedOrigins</c>), a built application served by BaGet (<c>Frontend:SpaPath</c>,
/// <c>Frontend:SpaBasePath</c>) that never shadows the APIs or the MVC screens, and a host without the MVC screens (<c>Frontend:Mvc</c>).
/// </summary>
public class UiApiHostingTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();
    private readonly string _spa = Path.Combine(Path.GetTempPath(), "BaGetSpaTests", Guid.NewGuid().ToString("N"));

    public UiApiHostingTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(Path.Combine(_spa, "assets"));
        File.WriteAllText(Path.Combine(_spa, "index.html"), "<html><body>SPA-INDEX</body></html>");
        File.WriteAllText(Path.Combine(_spa, "main.js"), "console.log('SPA-MAIN');");
        File.WriteAllText(Path.Combine(_spa, "assets", "site.css"), "body{}");
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
        try { Directory.Delete(_spa, recursive: true); } catch (IOException) { }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private (BaGetApplication App, HttpClient Client) Start(Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { { "Accounts:Enabled", "true" } };
        foreach (var pair in extra ?? new Dictionary<string, string>()) settings[pair.Key] = pair.Value;

        var app = new BaGetApplication(_output, configOverrides: settings);
        var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _disposables.Add(app);
        _disposables.Add(client);

        return (app, client);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? origin = null, string? preflightMethod = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (origin != null) request.Headers.Add("Origin", origin);
        if (preflightMethod != null)
        {
            request.Headers.Add("Access-Control-Request-Method", preflightMethod);
            request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-xsrf-token");
        }

        return await client.SendAsync(request, Token);
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    // ----- CORS

    [Fact]
    public async Task WithoutAListAnyOriginIsAllowedButNeverWithCredentials()
    {
        var (_, client) = Start();

        using var preflight = await SendAsync(client, HttpMethod.Options, "/api/ui/stats", "https://anywhere.example", "POST");
        using var actual = await SendAsync(client, HttpMethod.Get, "/api/ui/stats", "https://anywhere.example");

        // The endpoints that sign a visitor in are same-origin only without a list: no other page can script them from a visitor's browser.
        foreach (var path in new[] { "/api/ui/session", "/api/ui/session/refresh", "/api/ui/antiforgery", "/api/ui/account/register", "/api/ui/account/forgotpassword" })
        {
            using var sessionPreflight = await SendAsync(client, HttpMethod.Options, path, "https://anywhere.example", "POST");
            Assert.True(Header(sessionPreflight, "Access-Control-Allow-Origin") == null, path + " allowed another origin");
        }

        Assert.Equal("*", Header(preflight, "Access-Control-Allow-Origin"));
        Assert.Null(Header(preflight, "Access-Control-Allow-Credentials"));
        Assert.Equal("*", Header(actual, "Access-Control-Allow-Origin"));
        Assert.Null(Header(actual, "Access-Control-Allow-Credentials"));
        Assert.Contains(SessionHeaders.AccessToken, Header(actual, "Access-Control-Expose-Headers") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithAListOnlyThoseOriginsAreAllowedAndMaySendCredentials()
    {
        var (_, client) = Start(new Dictionary<string, string> { { "Cors:AllowedOrigins", "https://spa.example.com; https://other.example.com/" } });

        using var allowed = await SendAsync(client, HttpMethod.Options, "/api/ui/session", "https://spa.example.com", "POST");
        using var second = await SendAsync(client, HttpMethod.Get, "/api/ui/stats", "https://other.example.com");
        using var refused = await SendAsync(client, HttpMethod.Options, "/api/ui/session", "https://evil.example", "POST");
        using var refusedActual = await SendAsync(client, HttpMethod.Get, "/api/ui/stats", "https://evil.example");

        // What listing an origin grants: credentialed access to /api/ui only. The protocol routes keep "any origin, no credentials".
        using var protocol = await SendAsync(client, HttpMethod.Get, "/v3/index.json", "https://spa.example.com");
        using var protocolPreflight = await SendAsync(client, HttpMethod.Options, "/v3/index.json", "https://spa.example.com", "GET");
        using var protocolEvil = await SendAsync(client, HttpMethod.Get, "/v3/index.json", "https://evil.example");
        Assert.Equal("*", Header(protocol, "Access-Control-Allow-Origin"));
        Assert.Null(Header(protocol, "Access-Control-Allow-Credentials"));
        Assert.Null(Header(protocolPreflight, "Access-Control-Allow-Credentials"));
        Assert.Equal("*", Header(protocolEvil, "Access-Control-Allow-Origin"));

        Assert.Equal("https://spa.example.com", Header(allowed, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(allowed, "Access-Control-Allow-Credentials"));
        Assert.Contains("x-xsrf-token", Header(allowed, "Access-Control-Allow-Headers") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://other.example.com", Header(second, "Access-Control-Allow-Origin"));
        Assert.Null(Header(refused, "Access-Control-Allow-Origin"));
        Assert.Null(Header(refusedActual, "Access-Control-Allow-Origin"));
        Assert.Null(Header(refusedActual, "Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("not an origin")]
    [InlineData("https://spa.example.com/app")]
    [InlineData("ftp://spa.example.com")]
    public void AnAllowedOriginThatIsNotAnOriginFailsAtStartup(string origin)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "Cors:AllowedOrigins", origin } }).Build();

        var failure = Assert.Throws<InvalidOperationException>(() => new ConfigureBaGetOptions(configuration).Configure(new CorsOptions()));

        Assert.Contains("Cors:AllowedOrigins", failure.Message, StringComparison.Ordinal);
    }

    // ----- a single-page application served by BaGet

    [Fact]
    public async Task ABuiltApplicationIsServedUnderItsBasePathWithTheIndexAsTheFallback()
    {
        var (_, client) = Start(new Dictionary<string, string> { { "Frontend:SpaPath", _spa } });

        foreach (var path in new[] { "/app", "/app/", "/app/index.html", "/app/some/client/route", "/app/users/42" })
        {
            using var response = await client.GetAsync(path, Token);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {response.StatusCode}");
            Assert.Contains("SPA-INDEX", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        }

        using var index = await client.GetAsync("/app/", Token);
        Assert.Equal("no-cache", index.Headers.CacheControl?.ToString());

        using var script = await client.GetAsync("/app/main.js", Token);
        Assert.Contains("SPA-MAIN", await script.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        using var nested = await client.GetAsync("/app/assets/site.css", Token);
        Assert.Equal(HttpStatusCode.OK, nested.StatusCode);

        // A missing file is a 404, not the index; only a GET falls back.
        using var missing = await client.GetAsync("/app/missing.js", Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var post = await client.PostAsync("/app/some/route", new StringContent("{}"), Token);
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
    }

    [Fact]
    public async Task TheApplicationNeverShadowsTheApisTheProtocolsOrTheScreens()
    {
        var (app, client) = Start(new Dictionary<string, string> { { "Frontend:SpaPath", _spa } });
        await CreateUserAsync(app, "alice");

        using var home = await client.GetAsync("/", Token);
        using var api = await client.GetAsync("/api/ui/stats", Token);
        using var unknownApi = await client.GetAsync("/api/ui/does-not-exist", Token);
        using var unknownProtocol = await client.GetAsync("/v3/does-not-exist", Token);
        using var serviceIndex = await client.GetAsync("/v3/index.json", Token);
        using var login = await client.GetAsync("/account/login", Token);
        using var session = await client.PostAsJsonAsync("/api/ui/session", new { userName = "alice", password = Password }, Token);

        Assert.DoesNotContain("SPA-INDEX", await home.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal("application/json", api.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, unknownApi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownProtocol.StatusCode);
        Assert.Equal(HttpStatusCode.OK, serviceIndex.StatusCode);
        Assert.DoesNotContain("SPA-INDEX", await login.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    [Fact]
    public async Task TheBasePathIsConfigurable()
    {
        var (_, client) = Start(new Dictionary<string, string> { { "Frontend:SpaPath", _spa }, { "Frontend:SpaBasePath", "ui/" } });

        using var served = await client.GetAsync("/ui/deep/link", Token);
        using var old = await client.GetAsync("/app/deep/link", Token);

        Assert.Contains("SPA-INDEX", await served.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api")]
    [InlineData("/api/ui")]
    [InlineData("/v3")]
    [InlineData("/r/second")]
    [InlineData("/admin")]
    public void TheApplicationCannotBeServedAtTheRootOrWhereBaGetAnswers(string basePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Frontend:SpaPath", _spa }, { "Frontend:SpaBasePath", basePath } })
            .Build();

        var failure = Assert.Throws<InvalidOperationException>(() => configuration.ReadFrontendOptions());

        Assert.Contains("Frontend:SpaBasePath", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderWithoutAnIndexFailsAtStartup()
    {
        var empty = Path.Combine(_spa, "assets");

        var failure = Record.Exception(() => Start(new Dictionary<string, string> { { "Frontend:SpaPath", empty } }));

        Assert.NotNull(failure);
        Assert.Contains("index.html", failure!.ToString(), StringComparison.Ordinal);
    }

    // ----- a host without the MVC screens

    [Fact]
    public async Task WithoutTheMvcScreensTheHostServesTheApisAndTheApplicationOnly()
    {
        var (app, client) = Start(new Dictionary<string, string> { { "Frontend:Mvc", "false" }, { "Frontend:SpaPath", _spa } });
        await CreateUserAsync(app, "alice");

        using var home = await client.GetAsync("/", Token);
        using var login = await client.GetAsync("/account/login", Token);
        using var packages = await client.GetAsync("/packages", Token);
        using var serviceIndex = await client.GetAsync("/v3/index.json", Token);
        using var terms = await client.GetAsync("/api/ui/stats", Token);
        using var session = await client.PostAsJsonAsync("/api/ui/session", new { userName = "alice", password = Password }, Token);
        using var spa = await client.GetAsync("/app/anything", Token);

        Assert.Equal(HttpStatusCode.NotFound, home.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, packages.StatusCode);
        Assert.Equal(HttpStatusCode.OK, serviceIndex.StatusCode);
        Assert.Equal(HttpStatusCode.OK, terms.StatusCode);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Contains("SPA-INDEX", await spa.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not an origin")]
    [InlineData("https://spa.example.com/app")]
    public void AnInvalidOriginStopsTheHostAtStartupNotAtTheFirstRequest(string origin)
    {
        var failure = Record.Exception(() => Start(new Dictionary<string, string> { { "Cors:AllowedOrigins", origin } }));

        Assert.NotNull(failure);
        Assert.Contains("Cors:AllowedOrigins", failure!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheApplicationCannotBeServedWhereAScreenOrAProtocolAlreadyAnswers()
    {
        // "npm-packages" is a screen's first segment that no fixed list names: the mapped routes decide.
        var failure = Record.Exception(() => Start(new Dictionary<string, string> { { "Frontend:SpaPath", _spa }, { "Frontend:SpaBasePath", "/npm-packages" } }));

        Assert.NotNull(failure);
        Assert.Contains("npm-packages", failure!.ToString(), StringComparison.Ordinal);
    }
}
