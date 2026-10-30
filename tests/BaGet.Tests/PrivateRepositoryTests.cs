using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
/// B2 and the enforcement points of U2: a repository that is not open to everyone answers anonymous callers
/// <c>401</c> with a Basic challenge on every protocol route (which makes NuGet clients send credentials), a signed
/// in caller without Read <c>404</c>, and a caller with Read is served; write permissions are checked in front of
/// package ownership; pages follow the same Read check.
/// </summary>
public class PrivateRepositoryTests : IDisposable
{
    private const string Second = "second";
    private const string GlobalKey = "global-key";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;
    private string _aliceKey = string.Empty;
    private string _bobKey = string.Empty;
    private string _rootKey = string.Empty;

    public PrivateRepositoryTests(ITestOutputHelper output)
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

    /// <summary>
    /// <c>second</c> is private: alice may read it and deploy Contoso.* there, bob may do neither, root is an administrator.
    /// The seeded "Authenticated users deploy" target is removed, so Deploy comes from explicit grants only.
    /// </summary>
    private async Task SetUpAsync()
    {
        await _app.CreateRepositoryAsync(Second);
        foreach (var name in new[] { "alice", "bob" }) await CreateUserAsync(_app, name);
        await CreateUserAsync(_app, "root", administrator: true);

        using var scope = _app.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        async Task<string> KeyAsync(string user)
            => await keys.CreateAsync((await FindAsync(_app, user))!.Id, "test", ApiKeyScopes.All, "*", null, CancellationToken.None);
        _aliceKey = await KeyAsync("alice");
        _bobKey = await KeyAsync("bob");
        _rootKey = await KeyAsync("root");

        var alice = (await FindAsync(_app, "alice"))!;
        var administration = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
        await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync(Second, false, CancellationToken.None);
        await administration.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, CancellationToken.None);
        await administration.CreateTargetAsync("alice on second",new[] { Second }, new[] { "Contoso.*" }, null, null, null, CancellationToken.None);
        await administration.SetGrantAsync("alice on second", PrincipalType.User, alice.Id, AccessAction.Read | AccessAction.Deploy, null, CancellationToken.None);
        // Read for the whole repository, so the feed opens; the package-level Deploy above stays Contoso.* only.
        await administration.CreateTargetAsync("alice reads second", new[] { Second }, null, null, null, null, CancellationToken.None);
        await administration.SetGrantAsync("alice reads second", PrincipalType.User, alice.Id, AccessAction.Read, null, CancellationToken.None);
    }

    private static string Basic(string user, string password)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));

    private async Task<HttpResponseMessage> GetAsync(string path, string? user = null, string? password = null, string? apiKeyHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (password != null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Basic(user ?? "any", password));
        if (apiKeyHeader != null) request.Headers.Add("X-NuGet-ApiKey", apiKeyHeader);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static readonly string[] ProtocolPaths =
    {
        "v3/index.json",
        "v3/search?q=",
        "v3/autocomplete?q=a",
        "v3/dependents?packageId=x",
        "v3/package/contoso.lib/index.json",
        "v3/package/contoso.lib/1.0.0/contoso.lib.1.0.0.nupkg",
        "v3/registration/contoso.lib/index.json",
        "v3/registration/contoso.lib/1.0.0.json",
        "v3/catalog/index.json",
        "v3/vulnerabilities/index.json",
        "v3/repository-signatures/index.json",
        "api/v2",
        "api/v2/$metadata",
        "api/v2/Packages()",
        "api/v2/FindPackagesById()?id='Contoso.Lib'",
        "api/download/symbols/contoso.pdb/0123456789abcdef0123456789abcdef1/contoso.pdb",
    };

    public static IEnumerable<object[]> Paths() => ProtocolPaths.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task AnAnonymousCallerIsChallengedOnEveryRouteOfAPrivateRepository(string path)
    {
        using var response = await GetAsync("r/second/" + path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Basic", challenge.Scheme);
        Assert.Contains("realm=\"BaGet\"", challenge.Parameter);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task TheDefaultRepositoryStaysOpenOnTheSameRoutes(string path)
    {
        using var response = await GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    public static IEnumerable<object[]> ReadRoutesWithOtherMethods()
    {
        foreach (var method in new[] { "POST", "PUT", "PATCH", "OPTIONS" })
        {
            foreach (var path in new[]
            {
                "v3/search?q=",
                "v3/autocomplete?q=a",
                "v3/dependents?packageId=x",
                "v3/package/contoso.lib/index.json",
                "v3/package/contoso.lib/1.0.0/contoso.lib.1.0.0.nupkg",
                "api/download/symbols/contoso.pdb/0123456789abcdef0123456789abcdef1/contoso.pdb",
            })
            {
                yield return new object[] { method, path };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ReadRoutesWithOtherMethods))]
    public async Task AReadRouteIsProtectedWhateverTheHttpMethod(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "r/second/" + path);
        using var anonymous = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var signedIn = new HttpRequestMessage(new HttpMethod(method), "r/second/" + path);
        signedIn.Headers.Authorization = new AuthenticationHeaderValue("Basic", Basic("bob", _bobKey));
        using var denied = await _client.SendAsync(signedIn, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task APackagePageFollowsTheCallersPackagePatterns()
    {
        await _app.AddPackageAsync(TestPackages.Build("Contoso.Lib", "1.0.0"), CancellationToken.None, Second);
        await _app.AddPackageAsync(TestPackages.Build("Secret.Lib", "1.0.0"), CancellationToken.None, Second);
        var carol = await CreateCarolAsync();
        using (var scope = _app.Services.CreateScope())
        {
            var administration = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await administration.CreateTargetAsync("carol", new[] { Second }, new[] { "*" }, new[] { "Secret.*" }, null, null, CancellationToken.None);
            await administration.SetGrantAsync("carol", PrincipalType.User, carol.Id, AccessAction.Read, null, CancellationToken.None);
        }

        using var browser = new Browser(_app);
        await SignInAsync(browser, "carol");
        using var allowed = await browser.GetAsync("r/second/packages/Contoso.Lib");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var denied = await browser.GetAsync("r/second/packages/Secret.Lib");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var deniedAbuse = await browser.GetAsync("r/second/packages/Secret.Lib/1.0.0/report-abuse");
        Assert.Equal(HttpStatusCode.NotFound, deniedAbuse.StatusCode);
    }

    [Fact]
    public async Task APasswordThatIsAnApiKeyOpensTheRepositoryForItsOwner()
    {
        using var basic = await GetAsync("r/second/v3/index.json", "alice", _aliceKey);
        Assert.Equal(HttpStatusCode.OK, basic.StatusCode);

        // Some clients send the key in the NuGet header instead.
        using var header = await GetAsync("r/second/v3/search?q=", apiKeyHeader: _aliceKey);
        Assert.Equal(HttpStatusCode.OK, header.StatusCode);

        // The user name is the key owner's, whatever its case.
        using var upper = await GetAsync("r/second/v3/index.json", "ALICE", _aliceKey);
        Assert.Equal(HttpStatusCode.OK, upper.StatusCode);

        // The global key and an administrator's key read everything.
        using var global = await GetAsync("r/second/v3/index.json", "x", GlobalKey);
        Assert.Equal(HttpStatusCode.OK, global.StatusCode);
        using var root = await GetAsync("r/second/v3/index.json", "root", _rootKey);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
    }

    [Fact]
    public async Task WrongCredentialsAreChallengedAgain()
    {
        foreach (var (user, password) in new[] { ("alice", "bgt_not-a-key"), ("bob", _aliceKey), ("alice", ""), ("alice", GlobalKey + "x") })
        {
            using var response = await GetAsync("r/second/v3/index.json", user, password);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEmpty(response.Headers.WwwAuthenticate);
        }
    }

    [Fact]
    public async Task ASignedInCallerWithoutReadGetsNotFoundNotUnauthorized()
    {
        foreach (var path in ProtocolPaths)
        {
            using var response = await GetAsync("r/second/" + path, "bob", _bobKey);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(response.Headers.WwwAuthenticate);
        }

        // The same answer as a repository that does not exist.
        using var unknown = await GetAsync("r/nope/v3/index.json", "bob", _bobKey);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task APushIsRefusedWithoutDeployAndWithoutAMatchingPackageGrant()
    {
        async Task<HttpStatusCode> PushAsync(string id, string key)
        {
            using var package = TestPackages.Build(id, "1.0.0");
            using var request = TestPackages.PushRequest(package, key);
            request.RequestUri = new Uri("r/second/api/v2/package", UriKind.Relative);
            using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Created, await PushAsync("Contoso.Lib", _aliceKey));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Other.Lib", _aliceKey));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Contoso.Lib2", _bobKey));
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Other.Lib", _rootKey));
    }

    [Fact]
    public async Task APackageThatTheGrantExcludesIsNotFoundForTheCallerButServedToAdministrators()
    {
        await _app.AddPackageAsync(TestPackages.Build("Contoso.Lib", "1.0.0"), CancellationToken.None, Second);
        using var scope = _app.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
        var carol = await CreateCarolAsync();
        await administration.CreateTargetAsync("carol", new[] { Second }, new[] { "Contoso.*" }, null, null, null, CancellationToken.None);
        await administration.SetGrantAsync("carol", PrincipalType.User, carol.Id, AccessAction.Read, null, CancellationToken.None);

        using var allowed = await GetAsync("r/second/v3/registration/contoso.lib/index.json", "carol", carol.Key);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var denied = await GetAsync("r/second/v3/registration/other.lib/index.json", "carol", carol.Key);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var admin = await GetAsync("r/second/v3/registration/contoso.lib/index.json", "root", _rootKey);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    private async Task<(string Id, string Key)> CreateCarolAsync()
    {
        await CreateUserAsync(_app, "carol");
        var user = (await FindAsync(_app, "carol"))!;
        using var scope = _app.Services.CreateScope();
        var key = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync(user.Id, "carol", ApiKeyScopes.All, "*", null, CancellationToken.None);
        return (user.Id, key);
    }

    [Fact]
    public async Task APrivateRepositoryPageSendsAnAnonymousVisitorToSignInAndHidesItFromOthers()
    {
        using var anonymous = await _client.GetAsync("r/second/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("account/login", anonymous.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var bobBrowser = new Browser(_app);
        await SignInAsync(bobBrowser, "bob");
        using var denied = await bobBrowser.GetAsync("r/second/");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("repository-switcher", await bobBrowser.GetPageAsync("stats"));

        using var aliceBrowser = new Browser(_app);
        await SignInAsync(aliceBrowser, "alice");
        using var allowed = await aliceBrowser.GetAsync("r/second/");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains("repository-switcher", await aliceBrowser.GetPageAsync("stats"));
    }

    [Fact]
    public async Task AnAnonymousVisitorStillReadsTheOpenRepositoryPages()
    {
        using var open = await _client.GetAsync("", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
    }
}
