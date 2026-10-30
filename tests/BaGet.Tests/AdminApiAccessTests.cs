using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Mvc.UI;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Web.V3.Api.Catalog;

namespace BaGet.Tests;

/// <summary>
/// Who may call <c>/api/v1</c> and how: administrator keys with the <c>admin:api</c> scope or the global key, never a cookie session,
/// the password confirmation of the operations whose page asks for it, the read-only gate, ProblemDetails errors, paging, and the
/// OpenAPI document with the UI-coverage audit (every operation names a page that exists and is listed in docs/ui-coverage.md).
/// </summary>
public class AdminApiAccessTests : IDisposable
{
    private readonly AdminApiHarness _api;

    public AdminApiAccessTests(ITestOutputHelper output)
    {
        _api = new AdminApiHarness(output);
    }

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task WithoutCredentialsTheAnswerIs401WithABearerChallengeAndAProblem()
    {
        var answer = await _api.GetAsync("/api/v1/repositories", key: "");

        Assert.Equal(HttpStatusCode.Unauthorized, answer.Status);
        Assert.Equal("application/problem+json", answer.ContentType);
        Assert.Equal("unauthorized", answer.Code);
        Assert.Contains(answer.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task TheGlobalKeyAndAnAdministratorsAdminKeyAreAccepted()
    {
        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/v1/repositories", AdminApiHarness.GlobalKey)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/v1/repositories")).Status);

        // The key as the Basic password, as every other write route of the server accepts it.
        Assert.Equal(HttpStatusCode.OK, (await _api.SendAsync(HttpMethod.Get, "/api/v1/repositories", bearer: false)).Status);
    }

    [Fact]
    public async Task AKeyWithoutTheAdminScopeIsForbiddenEvenForAnAdministrator()
    {
        var packageKey = await _api.KeyAsync("root", ApiKeyScopes.All);

        var answer = await _api.GetAsync("/api/v1/repositories", packageKey);

        Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
        Assert.Equal("forbidden", answer.Code);
    }

    [Fact]
    public async Task OnlyAnAdministratorCanCreateAKeyWithTheAdminScope()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _api.KeyAsync("alice"));
        var packageKey = await _api.KeyAsync("alice", ApiKeyScopes.All);

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.GetAsync("/api/v1/repositories", packageKey)).Status);
    }

    [Fact]
    public async Task ADemotedAdministratorsKeyStopsWorkingAtOnce()
    {
        await CreateUserAsync(_api.App, "temp", administrator: true);
        var key = await _api.KeyAsync("temp");
        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/v1/repositories", key)).Status);

        await _api.WithAsync(async p => await p.GetRequiredService<AdministratorService>().DemoteAsync("temp", "root", AdminApiHarness.Token));

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.GetAsync("/api/v1/repositories", key)).Status);
    }

    [Fact]
    public async Task AKeyLimitedToARepositoryCannotAdministerTheServer()
    {
        await _api.App.CreateRepositoryAsync("second", AdminApiHarness.Token);
        var limited = await _api.KeyAsync("root", repository: "second");

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.GetAsync("/api/v1/repositories", limited)).Status);
    }

    [Fact]
    public async Task ASignedInBrowserSessionIsNeverAccepted()
    {
        using var browser = new Browser(_api.App);
        await SignInAsync(browser, "root");

        using var response = await browser.GetAsync("api/v1/repositories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AServerWithNoGlobalKeyAndNoAccountsNeverOpensTheApiToEverybody()
    {
        using var app = new BaGetApplication(
            TestContext.Current.TestOutputHelper!,
            configOverrides: new Dictionary<string, string> { { "ApiKey", "" }, { "Accounts:Enabled", "false" } });
        using var client = app.CreateClient();

        using var anonymous = await client.GetAsync("/api/v1/repositories", AdminApiHarness.Token);

        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
    }

    [Fact]
    public async Task ARemoteRepositoryNeedsTheOwnersPasswordLikeItsPageAndTheGlobalKeyNeedsNone()
    {
        var body = new { name = "up", format = "NuGet", kind = "Remote", remote = new { url = "https://example.invalid/v3/index.json" } };

        var missing = await _api.PostAsync("/api/v1/repositories", body);
        Assert.Equal(HttpStatusCode.Forbidden, missing.Status);
        Assert.Equal("confirmation-required", missing.Code);

        var wrong = await _api.PostAsync("/api/v1/repositories", body, password: "not-it");
        Assert.Equal("confirmation-required", wrong.Code);
        Assert.Null(await FindRepositoryAsync("up"));

        var created = await _api.PostAsync("/api/v1/repositories", body, password: Password);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal("Remote", created.Json["kind"]!.GetValue<string>());
        Assert.EndsWith("/api/v1/repositories/up", created.Headers.Location!.ToString());

        // The global key is the confirmation.
        var other = await _api.PostAsync("/api/v1/repositories", new { name = "up2", kind = "Remote", remote = new { url = "https://example.invalid/v3/index.json" } }, AdminApiHarness.GlobalKey);
        Assert.Equal(HttpStatusCode.Created, other.Status);
    }

    [Fact]
    public async Task WhileTheServerIsReadOnlyWritesAnswer503WithRetryAfterAndReadsStillWork()
    {
        var gate = _api.App.Services.GetRequiredService<MaintenanceGate>();
        gate.Close("test");
        try
        {
            var write = await _api.PostAsync("/api/v1/repositories", new { name = "late" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, write.Status);
            Assert.Equal("read-only", write.Code);
            Assert.True(write.Headers.Contains("Retry-After"));

            Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/v1/repositories")).Status);
        }
        finally
        {
            gate.Open();
        }
    }

    [Fact]
    public async Task ListsArePagedAndErrorsAreProblems()
    {
        foreach (var name in new[] { "a1", "a2", "a3" }) await _api.App.CreateRepositoryAsync(name, AdminApiHarness.Token);

        var first = await _api.GetAsync("/api/v1/repositories?pageSize=2&page=1");
        var second = await _api.GetAsync("/api/v1/repositories?pageSize=2&page=2");

        Assert.Equal(4, first.Json["total"]!.GetValue<int>());
        Assert.Equal(2, first.Json["items"]!.AsArray().Count);
        Assert.Equal(2, second.Json["items"]!.AsArray().Count);
        Assert.Equal(new[] { "a1", "a2" }, first.Json["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()).ToArray());

        var missing = await _api.GetAsync("/api/v1/repositories/nope");
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal("application/problem+json", missing.ContentType);

        var malformed = await _api.SendAsync(HttpMethod.Post, "/api/v1/repositories", "{ not json");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.Status);
    }

    [Fact]
    public async Task TheOpenApiDocumentListsEveryOperationAndEveryOperationHasAPageAndADocsRow()
    {
        var answer = await _api.GetAsync("/api/v1/openapi.json");
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.StartsWith("3.0", answer.Json["openapi"]!.GetValue<string>());

        var provider = _api.App.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        Assert.Empty(ApiCatalog.Undescribed(provider));
        var endpoints = ApiCatalog.Endpoints(provider);
        Assert.NotEmpty(endpoints);

        var paths = answer.Json["paths"]!.AsObject();
        // A screen is a controller action of a view controller, named by its controller and the action that shows it.
        var pages = provider.ActionDescriptors.Items
            .Select(a => ScreenEndpoints.ScreenOf(a)?.ToString())
            .Where(s => s != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var docs = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "ui-coverage.md"));
        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(paths[endpoint.Path]?[endpoint.Method.ToLowerInvariant()]);
            Assert.True(pages.Contains(endpoint.Operation.UiScreen.ToString()), $"{endpoint.Method} {endpoint.Path}: the screen {endpoint.Operation.UiScreen} does not exist.");
            Assert.True(docs.Contains($"`{endpoint.Method} {endpoint.Path}`", StringComparison.Ordinal), $"docs/ui-coverage.md does not list `{endpoint.Method} {endpoint.Path}`.");
        }
    }

    [Fact]
    public async Task EveryApiRouteIsProtectedByTheAdministratorCheck()
    {
        var provider = _api.App.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        foreach (var endpoint in ApiCatalog.Endpoints(provider))
        {
            // The path with every parameter filled in: the answer for a caller without credentials must be 401 before anything else.
            var path = System.Text.RegularExpressions.Regex.Replace(endpoint.Path, @"\{([^}]+)\}", m => m.Groups[1].Value == "key" ? "1" : "x");
            var answer = await _api.SendAsync(new HttpMethod(endpoint.Method), path, endpoint.Method is "GET" or "DELETE" ? null : new { }, key: "");

            Assert.True(answer.Status == HttpStatusCode.Unauthorized, $"{endpoint.Method} {endpoint.Path} answered {(int)answer.Status} without credentials.");
        }
    }

    private async Task<JsonNode?> FindRepositoryAsync(string name)
    {
        var answer = await _api.GetAsync("/api/v1/repositories/" + name);

        return answer.Status == HttpStatusCode.OK ? answer.Json : null;
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BaGet.sln"))) directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("BaGet.sln not found above the test output.");
    }
}

/// <summary>The REST API page and the key form that creates the <c>admin:api</c> key (the UI side of the API).</summary>
public class AdminApiPageTests : IDisposable
{
    private readonly AdminApiHarness _api;

    public AdminApiPageTests(ITestOutputHelper output)
    {
        _api = new AdminApiHarness(output);
    }

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task TheApiPageListsEveryOperationForAdministratorsOnlyAndTheMenuLinksToIt()
    {
        using var root = new Browser(_api.App);
        await SignInAsync(root, "root");
        using var alice = new Browser(_api.App);
        await SignInAsync(alice, "alice");

        var page = await root.GetPageAsync("admin/api");
        var endpoints = ApiCatalog.Endpoints(_api.App.Services.GetRequiredService<IActionDescriptorCollectionProvider>());
        Assert.All(endpoints, e => Assert.Contains($"data-path=\"{e.Path}\" data-method=\"{e.Method}\"", page));
        Assert.Contains("/api/v1/openapi.json", page);
        Assert.Contains("X-Confirm-Password", page);
        Assert.Contains("href=\"/admin/api\"", await root.GetPageAsync(""));

        using var denied = await alice.GetAsync("admin/api");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task OnlyAdministratorsAreOfferedTheAdminScopeWhenCreatingAKey()
    {
        using var root = new Browser(_api.App);
        await SignInAsync(root, "root");
        using var alice = new Browser(_api.App);
        await SignInAsync(alice, "alice");

        Assert.Contains("value=\"admin:api\"", await root.GetPageAsync("account/apikeys"));
        Assert.DoesNotContain("admin:api", await alice.GetPageAsync("account/apikeys"));
    }

    [Fact]
    public async Task AWrongConfirmationPasswordCountsAsAFailedSignIn()
    {
        var user = await FindAsync(_api.App, "root");
        Assert.Equal(0, user!.AccessFailedCount);

        var wrong = await _api.PostAsync("/api/v1/repositories", new { name = "up", kind = "Remote", remote = new { url = "https://example.invalid/v3/index.json" } }, password: "wrong");
        Assert.Equal("confirmation-required", wrong.Code);

        Assert.Equal(1, (await FindAsync(_api.App, "root"))!.AccessFailedCount);
    }
}
