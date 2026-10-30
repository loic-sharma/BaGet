using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The access model in front of virtual repositories: reads need Read on the virtual repository and include only the
/// members the caller may read; writes go to the deploy repository and are authorized there. <c>pub</c> is open,
/// <c>priv</c> is private (alice reads it and deploys Contoso.* to it, bob has no grant), <c>v</c> is [priv, pub] and
/// deploys to <c>priv</c>.
/// </summary>
public class VirtualRepositoryAccessTests : IDisposable
{
    private const string GlobalKey = "global-key";
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;
    private string _aliceKey = string.Empty;
    private string _bobKey = string.Empty;

    public VirtualRepositoryAccessTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", GlobalKey },
            { "Accounts:Enabled", "true" },
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        SetUpAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private async Task SetUpAsync()
    {
        await _app.CreateRepositoryAsync("pub");
        await _app.CreateRepositoryAsync("priv");
        foreach (var name in new[] { "alice", "bob" }) await CreateUserAsync(_app, name);

        using var scope = _app.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        async Task<string> KeyAsync(string user)
            => await keys.CreateAsync((await FindAsync(_app, user))!.Id, "test", ApiKeyScopes.All, "*", null, Cancel);
        _aliceKey = await KeyAsync("alice");
        _bobKey = await KeyAsync("bob");

        var alice = (await FindAsync(_app, "alice"))!;
        var access = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
        await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("priv", false, Cancel);
        await access.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, Cancel);
        await access.CreateTargetAsync("alice on priv", new[] { "priv" }, null, null, null, null, Cancel);
        await access.SetGrantAsync("alice on priv", PrincipalType.User, alice.Id, AccessAction.Read | AccessAction.Deploy | AccessAction.Annotate, null, Cancel);

        await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
            "v",
            new VirtualRepositorySettings { Members = { "priv", "pub" }, DeployRepository = "priv" },
            Cancel);

        await _app.AddPackageAsync(TestPackages.Build("Open.Lib", "1.0.0"), Cancel, "pub");
        await _app.AddPackageAsync(TestPackages.Build("Secret.Lib", "1.0.0"), Cancel, "priv");
    }

    private static string Basic(string user, string password)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));

    private async Task<HttpResponseMessage> GetAsync(string path, string? user = null, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Basic(user ?? "any", key));

        return await _client.SendAsync(request, Cancel);
    }

    private async Task<HttpStatusCode> PushAsync(string id, string? user, string key, string repository = "v")
    {
        using var package = TestPackages.Build(id, "1.0.0");
        using var request = TestPackages.PushRequest(package, key);
        request.RequestUri = new Uri($"r/{repository}/api/v2/package", UriKind.Relative);
        using var response = await _client.SendAsync(request, Cancel);

        return response.StatusCode;
    }

    [Fact]
    public async Task AnAnonymousCallerSeesOnlyTheMembersItMayRead()
    {
        using var open = await GetAsync("r/v/v3/registration/open.lib/index.json");
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);

        // The virtual repository is open, so no challenge: the private member simply does not contribute.
        using var secret = await GetAsync("r/v/v3/registration/secret.lib/index.json");
        Assert.Equal(HttpStatusCode.NotFound, secret.StatusCode);
        using var secretContent = await GetAsync("r/v/v3/package/secret.lib/1.0.0/secret.lib.1.0.0.nupkg");
        Assert.Equal(HttpStatusCode.NotFound, secretContent.StatusCode);
        using var secretVersions = await GetAsync("r/v/v3/package/secret.lib/index.json");
        Assert.Equal(HttpStatusCode.NotFound, secretVersions.StatusCode);

        using var search = await GetAsync("r/v/v3/search?q=");
        var ids = JsonDocument.Parse(await search.Content.ReadAsStringAsync(Cancel)).RootElement
            .GetProperty("data").EnumerateArray().Select(d => d.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { "Open.Lib" }, ids);

        using var v2 = await GetAsync("r/v/api/v2/FindPackagesById()?id='Secret.Lib'");
        Assert.DoesNotContain("Secret.Lib", await v2.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task ACallerWhoMayReadAMemberSeesItsPackagesThroughTheVirtualRepository()
    {
        using var secret = await GetAsync("r/v/v3/registration/secret.lib/index.json", "alice", _aliceKey);
        Assert.Equal(HttpStatusCode.OK, secret.StatusCode);
        using var download = await GetAsync("r/v/v3/package/secret.lib/1.0.0/secret.lib.1.0.0.nupkg", "alice", _aliceKey);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        using var search = await GetAsync("r/v/v3/search?q=", "alice", _aliceKey);
        var ids = JsonDocument.Parse(await search.Content.ReadAsStringAsync(Cancel)).RootElement
            .GetProperty("data").EnumerateArray().Select(d => d.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { "Open.Lib", "Secret.Lib" }, ids);

        // Bob is signed in but has no grant on priv: same as anonymous.
        using var bob = await GetAsync("r/v/v3/registration/secret.lib/index.json", "bob", _bobKey);
        Assert.Equal(HttpStatusCode.NotFound, bob.StatusCode);
    }

    [Fact]
    public async Task APrivateVirtualRepositoryChallengesAnonymousCallersAndHidesFromOthers()
    {
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("v", false, Cancel);
        }

        using var anonymous = await GetAsync("r/v/v3/index.json");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Basic", Assert.Single(anonymous.Headers.WwwAuthenticate).Scheme);

        // Read on the members does not open the virtual repository itself.
        using var alice = await GetAsync("r/v/v3/index.json", "alice", _aliceKey);
        Assert.Equal(HttpStatusCode.NotFound, alice.StatusCode);
    }

    [Fact]
    public async Task APushToTheVirtualRepositoryIsAuthorizedAgainstTheDeployRepository()
    {
        // Bob cannot deploy to priv, so he cannot deploy through v; the global key can (an administrator-grade key).
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Contoso.Bob", "bob", _bobKey));
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Contoso.Alice", "alice", _aliceKey));

        using var scope = _app.Services.CreateScope();
        var summaries = await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().ListAsync(Cancel);
        Assert.Equal(2, summaries.Single(s => s.Name == "priv").PackageCount);
        Assert.Equal(1, summaries.Single(s => s.Name == "pub").PackageCount);
        Assert.Equal(0, summaries.Single(s => s.Name == "v").PackageCount);

        using var alice = await GetAsync("r/v/v3/registration/contoso.alice/index.json", "alice", _aliceKey);
        Assert.Equal(HttpStatusCode.OK, alice.StatusCode);
    }

    [Fact]
    public async Task DeployOnTheVirtualRepositoryItselfGrantsNothing()
    {
        var bob = (await FindAsync(_app, "bob"))!;
        using (var scope = _app.Services.CreateScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await access.CreateTargetAsync("bob on v", new[] { "v" }, null, null, null, null, Cancel);
            await access.SetGrantAsync("bob on v", PrincipalType.User, bob.Id, AccessAction.All, null, Cancel);
        }

        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Contoso.Bob", "bob", _bobKey));

        using var scope2 = _app.Services.CreateScope();
        var evaluator = scope2.ServiceProvider.GetRequiredService<IAccessEvaluator>();
        var v = (await scope2.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync("v", Cancel))!;
        var principal = new BaGetPrincipal(bob.Id, bob.UserName, false, ApiKeyScopes.All, "*");
        Assert.True(await evaluator.IsAllowedAsync(principal, v, null, AccessAction.Read, Cancel));
        Assert.False(await evaluator.IsAllowedAsync(principal, v, null, AccessAction.Deploy, Cancel));
        Assert.Equal(AccessAction.None, (await evaluator.ExplainAsync(principal, v, null, Cancel)).Actions & AccessAction.Deploy);
    }

    [Fact]
    public async Task TheUploadPageOfAVirtualRepositoryIsOpenOnlyToThoseWhoCanDeployToItsDeployRepository()
    {
        using var aliceBrowser = new Browser(_app);
        await SignInAsync(aliceBrowser, "alice");
        var page = await aliceBrowser.GetPageAsync("r/v/upload");
        Assert.Contains("id=\"upload-virtual-note\"", page);
        Assert.Contains("deploy repository", page);

        using var bobBrowser = new Browser(_app);
        await SignInAsync(bobBrowser, "bob");
        using var denied = await bobBrowser.GetAsync("r/v/upload");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        // The picker offers v to alice (she can write to priv) and not to bob.
        using var scope = _app.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IRepositoryAccess>();
        var aliceUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, (await FindAsync(_app, "alice"))!.Id) }, "test"));
        Assert.Contains("v", (await access.WritableAsync(aliceUser, Cancel)).Select(r => r.Name));
    }

    [Fact]
    public async Task PagesThatOwnRepositoryDataAnswerNotFoundOnAVirtualRepository()
    {
        using var alice = new Browser(_app);
        await SignInAsync(alice, "alice");

        foreach (var path in new[] { "r/v/statistics", "r/v/packages/Open.Lib/1.0.0/report-abuse" })
        {
            using var response = await alice.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // The index and package pages work, and show the merged content.
        var index = await alice.GetPageAsync("r/v/packages");
        Assert.Contains("Open.Lib", index);
        Assert.Contains("Secret.Lib", index);
        var package = await alice.GetPageAsync("r/v/packages/Secret.Lib");
        Assert.Contains("Secret.Lib", package);
        Assert.DoesNotContain("Manage package", package);
        Assert.DoesNotContain("report-abuse", package);
    }
}
