using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// U4 / D6: an API key may be limited to some repositories (<see cref="ApiKeyRepository"/>). A key without a limit
/// works everywhere, as every key did before; a limited one is no credential in the other repositories (writes are
/// refused, reads are judged as an anonymous caller's), whoever its user is. The limit is chosen on the API keys page,
/// shown on it and on the administrator's user page, and cannot outlive its repository.
/// </summary>
public class ApiKeyRepositoryTests : IDisposable
{
    private const string Second = "second";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;
    private int _secondKey;

    public ApiKeyRepositoryTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
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
        await _app.CreateRepositoryAsync(Second);
        await CreateUserAsync(_app, "alice");
        await CreateUserAsync(_app, "root", administrator: true);

        using var scope = _app.Services.CreateScope();
        _secondKey = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Second, CancellationToken.None))!.Key;
    }

    private Task<string> KeyAsync(string user, params int[] repositories)
        => WithAsync(_app, async sp => await sp.GetRequiredService<ApiKeyService>().CreateAsync(
            (await FindAsync(_app, user))!.Id, "test", ApiKeyScopes.All, "*", null, repositories, CancellationToken.None));

    private async Task<HttpStatusCode> PushAsync(string repositoryPath, string id, string key)
    {
        using var package = TestPackages.Build(id, "1.0.0");
        using var request = TestPackages.PushRequest(package, key);
        request.RequestUri = new Uri(repositoryPath + "api/v2/package", UriKind.Relative);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private async Task<HttpStatusCode> GetStatusAsync(string path, string user, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + key)));
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private Task<int> KeyCountAsync()
        => WithAsync(_app, sp => sp.GetRequiredService<IContext>().ApiKeys!.CountAsync());

    [Fact]
    public async Task AKeyWithoutALimitWorksInEveryRepository()
    {
        var key = await KeyAsync("alice");

        Assert.Equal(HttpStatusCode.Created, await PushAsync("", "Free.Default", key));
        Assert.Equal(HttpStatusCode.Created, await PushAsync("r/second/", "Free.Second", key));
    }

    [Fact]
    public async Task ALimitedKeyPushesOnlyToItsRepositories()
    {
        var key = await KeyAsync("alice", _secondKey);

        Assert.Equal(HttpStatusCode.Created, await PushAsync("r/second/", "Limited.Second", key));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("", "Limited.Default", key));
    }

    [Fact]
    public async Task TheLimitHoldsForAnAdministratorsKeyToo()
    {
        var key = await KeyAsync("root", Repository.DefaultKey);

        Assert.Equal(HttpStatusCode.Created, await PushAsync("", "Admin.Default", key));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("r/second/", "Admin.Second", key));
    }

    [Fact]
    public async Task ALimitedKeyStillReadsWhatAnonymousCallersRead_ButNeverAPrivateRepository()
    {
        var alice = (await FindAsync(_app, "alice"))!;
        await WithAsync(_app, async sp =>
        {
            await sp.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync(Second, false, CancellationToken.None);
            var access = sp.GetRequiredService<IAccessAdministration>();
            await access.CreateTargetAsync("alice reads second", new[] { Second }, null, null, null, null, CancellationToken.None);
            await access.SetGrantAsync("alice reads second", PrincipalType.User, alice.Id, AccessAction.Read, null, CancellationToken.None);
            return 0;
        });
        var unlimited = await KeyAsync("alice");
        var defaultOnly = await KeyAsync("alice", Repository.DefaultKey);

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("r/second/v3/index.json", "alice", unlimited));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync("r/second/v3/index.json", "alice", defaultOnly));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("v3/index.json", "alice", defaultOnly));
    }

    [Fact]
    public async Task TheAuthenticatedPrincipalCarriesTheLimit()
    {
        var limited = await KeyAsync("alice", _secondKey);
        var free = await KeyAsync("alice");

        var (limitedPrincipal, freePrincipal) = await WithAsync(_app, async sp =>
        {
            var auth = sp.GetRequiredService<Core.IAuthenticationService>();
            return (await auth.AuthenticatePrincipalAsync(limited, CancellationToken.None),
                    await auth.AuthenticatePrincipalAsync(free, CancellationToken.None));
        });

        Assert.Equal(new[] { _secondKey }, limitedPrincipal!.RepositoryKeys);
        Assert.True(limitedPrincipal.AllowsRepository(_secondKey));
        Assert.False(limitedPrincipal.AllowsRepository(Repository.DefaultKey));
        Assert.Null(freePrincipal!.RepositoryKeys);
        Assert.True(freePrincipal.AllowsRepository(12345));
    }

    [Fact]
    public async Task AKeyCannotBeLimitedToARepositoryThatDoesNotExist()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => KeyAsync("alice", 9999));
        Assert.Equal(0, await KeyCountAsync());
    }

    [Fact]
    public async Task RevokingAKeyTakesItsLimitWithIt()
    {
        var alice = (await FindAsync(_app, "alice"))!;
        await KeyAsync("alice", _secondKey, Repository.DefaultKey);
        var key = (await WithAsync(_app, sp => sp.GetRequiredService<ApiKeyService>().ListAsync(alice.Id, CancellationToken.None))).Single();

        await WithAsync(_app, sp => sp.GetRequiredService<ApiKeyService>().RevokeAsync(alice.Id, key.Key, CancellationToken.None));

        Assert.Equal(0, await WithAsync(_app, sp => sp.GetRequiredService<IContext>().ApiKeyRepositories!.CountAsync()));
    }

    [Fact]
    public async Task DeletingARepositoryDeletesTheKeysLimitedToItAloneAndKeepsTheOthers()
    {
        var only = await KeyAsync("alice", _secondKey);
        var both = await KeyAsync("alice", _secondKey, Repository.DefaultKey);
        var free = await KeyAsync("alice");

        await WithAsync(_app, async sp =>
        {
            await sp.GetRequiredService<IRepositoryAdministration>().DeleteAsync(Second, CancellationToken.None);
            return 0;
        });

        var principals = await WithAsync(_app, async sp =>
        {
            var auth = sp.GetRequiredService<Core.IAuthenticationService>();
            return new[]
            {
                await auth.AuthenticatePrincipalAsync(only, CancellationToken.None),
                await auth.AuthenticatePrincipalAsync(both, CancellationToken.None),
                await auth.AuthenticatePrincipalAsync(free, CancellationToken.None),
            };
        });

        Assert.Null(principals[0]);
        Assert.Equal(new[] { Repository.DefaultKey }, principals[1]!.RepositoryKeys);
        Assert.Null(principals[2]!.RepositoryKeys);
    }

    // ---- pages

    private async Task<Browser> SignedInAsync(string user)
    {
        var browser = new Browser(_app);
        await SignInAsync(browser, user);
        return browser;
    }

    private static Dictionary<string, string> KeyForm(params (string, string)[] extra)
    {
        var form = new Dictionary<string, string>
        {
            { "Description", "ci" }, { "Scopes", ApiKeyScopes.Push }, { "PackageGlobPattern", "*" }, { "ExpiresInDays", "30" },
        };
        foreach (var (name, value) in extra) form[name] = value;
        return form;
    }

    [Fact]
    public async Task TheKeysPageCreatesALimitedKeyAndListsTheLimit()
    {
        using var alice = await SignedInAsync("alice");

        var page = await alice.GetPageAsync("account/apikeys");
        Assert.Contains("name=\"RepositoryKeys\"", page);

        using var created = await alice.PostAsync(
            "account/apikeys", KeyForm(("RepositoryKeys", _secondKey.ToString())), action: "account/apikeys?handler=Create");
        var html = await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var key = Regex.Match(html, "<pre id=\"created-key\">([^<]+)</pre>").Groups[1].Value;

        Assert.StartsWith("bgt_", key);
        Assert.Contains("<td>second</td>", html);
        Assert.Equal(HttpStatusCode.Created, await PushAsync("r/second/", "Page.Second", key));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("", "Page.Default", key));
    }

    [Fact]
    public async Task ANewKeyWithNoRepositoryTickedWorksEverywhereAndSaysSo()
    {
        using var alice = await SignedInAsync("alice");

        using var created = await alice.PostAsync("account/apikeys", KeyForm(), action: "account/apikeys?handler=Create");
        var html = await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("<td>all repositories</td>", html);
    }

    [Fact]
    public async Task ARepositoryTheUserCannotChooseIsRefused()
    {
        using var alice = await SignedInAsync("alice");

        using var created = await alice.PostAsync(
            "account/apikeys", KeyForm(("RepositoryKeys", "9999")), action: "account/apikeys?handler=Create");
        var html = await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("created-key", html);
        Assert.Equal(0, await KeyCountAsync());
    }

    [Fact]
    public async Task TheAdministratorsUserPageShowsEachKeysRepositories()
    {
        await KeyAsync("alice", _secondKey);
        await KeyAsync("alice");
        using var root = await SignedInAsync("root");

        var html = await root.GetPageAsync("admin/users/alice");

        Assert.Contains("<th>Repositories</th>", html);
        Assert.Contains("<td>second</td>", html);
        Assert.Contains("<td>all repositories</td>", html);
    }
}
