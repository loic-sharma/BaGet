using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// Accounts enabled on a closed server: the global key is the superuser, and users'
/// keys act within their scopes, their globs and the packages they own.
/// </summary>
public class AccountsIntegrationTests : IDisposable
{
    private const string GlobalKey = "global-key";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    // Keeps cookies and does not follow redirects, like a browser seen one hop at a time.
    private readonly HttpClient _browser;

    public AccountsIntegrationTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", GlobalKey },
            { "Accounts:Enabled", "true" },
        });
        _client = _app.CreateClient();
        _browser = _app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task TheGlobalKeyStillPushes()
    {
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Legacy.Package", "1.0.0", GlobalKey));
    }

    [Fact]
    public async Task AWrongKeyIsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Any.Package", "1.0.0", "bgt_wrong"));
    }

    [Fact]
    public async Task PushingANewIdMakesThePusherItsOwner()
    {
        var alice = await CreateUserAsync("alice");
        var key = await CreateKeyAsync(alice, ApiKeyScopes.Push);

        Assert.Equal(HttpStatusCode.Created, await PushAsync("Alice.Package", "1.0.0", key));
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Alice.Package", "2.0.0", key));

        var owners = await WithServiceAsync<PackageOwnershipService, IReadOnlyList<PackageOwner>>(
            s => s.GetOwnersAsync("alice.package", CancellationToken.None));
        Assert.Equal("alice", Assert.Single(owners).OwnerName);
    }

    [Fact]
    public async Task AnotherUserCannotPushToAnOwnedId()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));

        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Alice.Package", "2.0.0", await CreateKeyAsync(bob, ApiKeyScopes.Push)));
    }

    [Fact]
    public async Task AnIdWithoutOwnersIsForTheGlobalKeyOnly()
    {
        // Pushed before accounts existed, so it has no owner and nobody may claim it.
        await PushAsync("Legacy.Package", "1.0.0", GlobalKey);
        var alice = await CreateUserAsync("alice");

        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Legacy.Package", "2.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push)));
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Legacy.Package", "2.0.0", GlobalKey));
    }

    [Fact]
    public async Task PushVersionCannotClaimANewId()
    {
        var alice = await CreateUserAsync("alice");
        await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));
        var versionOnly = await CreateKeyAsync(alice, ApiKeyScopes.PushVersion);

        Assert.Equal(HttpStatusCode.Created, await PushAsync("Alice.Package", "2.0.0", versionOnly));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Alice.Other", "1.0.0", versionOnly));
    }

    [Fact]
    public async Task AKeyOnlyActsWithinItsGlob()
    {
        var alice = await CreateUserAsync("alice");
        var key = await CreateKeyAsync(alice, ApiKeyScopes.Push, glob: "Alice.*");

        Assert.Equal(HttpStatusCode.Created, await PushAsync("Alice.Package", "1.0.0", key));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Elsewhere.Package", "1.0.0", key));
    }

    [Fact]
    public async Task AnExpiredKeyIsUnauthorized()
    {
        var alice = await CreateUserAsync("alice");
        // The test host's clock is frozen at 2020-01-01.
        var key = await CreateKeyAsync(alice, ApiKeyScopes.Push, expires: new DateTime(2019, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Alice.Package", "1.0.0", key));
    }

    [Fact]
    public async Task ARevokedKeyIsUnauthorized()
    {
        var alice = await CreateUserAsync("alice");
        var key = await CreateKeyAsync(alice, ApiKeyScopes.Push);

        await WithServiceAsync<ApiKeyService, bool>(async s =>
        {
            var stored = Assert.Single(await s.ListAsync(alice.Id, CancellationToken.None));
            return await s.RevokeAsync(alice.Id, stored.Key, CancellationToken.None);
        });

        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Alice.Package", "1.0.0", key));
    }

    [Fact]
    public async Task UnlistingNeedsTheUnlistScopeAndOwnership()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));

        Assert.Equal(HttpStatusCode.Forbidden, await DeleteAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push)));
        Assert.Equal(HttpStatusCode.Forbidden, await DeleteAsync("Alice.Package", "1.0.0", await CreateKeyAsync(bob, ApiKeyScopes.Unlist)));
        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Unlist)));
    }

    [Fact]
    public async Task AnOrganizationMemberActsForTheOrganizationsPackages()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        await PushAsync("Contoso.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));

        await WithServiceAsync<PackageOwnershipService, bool>(async s =>
        {
            Assert.True(await s.CreateOrganizationAsync("contoso", alice.Id, CancellationToken.None));
            Assert.True(await s.AddMemberAsync("contoso", bob.Id, isAdmin: false, CancellationToken.None));
            Assert.Equal(OwnerChangeResult.Success, await s.AddOwnerAsync("Contoso.Package", "contoso", CancellationToken.None));
            return true;
        });

        Assert.Equal(HttpStatusCode.Created, await PushAsync("Contoso.Package", "2.0.0", await CreateKeyAsync(bob, ApiKeyScopes.PushVersion)));
    }

    [Fact]
    public async Task AnOrganizationCannotTakeAUsersName()
    {
        var alice = await CreateUserAsync("alice");

        Assert.False(await WithServiceAsync<PackageOwnershipService, bool>(
            s => s.CreateOrganizationAsync("Alice", alice.Id, CancellationToken.None)));
    }

    [Fact]
    public async Task ThePackagesLastOwnerCannotBeRemoved()
    {
        var alice = await CreateUserAsync("alice");
        await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));

        Assert.Equal(OwnerChangeResult.LastOwner, await WithServiceAsync<PackageOwnershipService, OwnerChangeResult>(
            s => s.RemoveOwnerAsync("Alice.Package", "alice", CancellationToken.None)));
    }

    [Fact]
    public async Task SearchReportsOwnersAndVerification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await CreateUserAsync("alice");
        await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));
        await PushAsync("Legacy.Package", "1.0.0", GlobalKey);
        await WithServiceAsync<PackageOwnershipService, bool>(
            s => s.SetVerifiedAsync("Alice.Package", verified: true, CancellationToken.None));

        using var search = System.Text.Json.JsonDocument.Parse(await _client.GetStringAsync("v3/search", cancellationToken));
        #pragma warning disable CS8621, CS8714
        var results = search.RootElement.GetProperty("data").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetString());
        #pragma warning restore CS8621, CS8714

        Assert.Equal("alice", results["Alice.Package"].GetProperty("owners")[0].GetString());
        Assert.True(results["Alice.Package"].GetProperty("verified").GetBoolean());
        Assert.False(results["Legacy.Package"].TryGetProperty("owners", out _));
        Assert.False(results["Legacy.Package"].TryGetProperty("verified", out _));
    }

    [Fact]
    public async Task ARegisteredUserCanCreateAKeyThatPushes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The whole browser flow: register, then create a key on the API keys page.
        var register = await GetFormAsync("account/register");
        using (var response = await PostFormAsync("account/register", register, new Dictionary<string, string>
               {
                   { "UserName", "carol" },
                   { "Password", "Passw0rd!" },
                   { "ConfirmPassword", "Passw0rd!" },
               }))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var keys = await GetFormAsync("account/apikeys");
        string key;
        using (var response = await PostFormAsync("account/apikeys?handler=Create", keys, new Dictionary<string, string>
               {
                   { "Description", "ci" },
                   { "Scopes", ApiKeyScopes.Push },
                   { "PackageGlobPattern", "*" },
                   { "ExpiresInDays", "30" },
               }))
        {
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            key = Regex.Match(html, "<pre id=\"created-key\">([^<]+)</pre>").Groups[1].Value;
        }

        Assert.StartsWith("bgt_", key);
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Carol.Package", "1.0.0", key));
    }

    [Fact]
    public async Task TheApiKeysPageNeedsASignedInUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _browser.GetAsync("account/apikeys", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        #pragma warning disable CS8602
        Assert.Contains("/account/login", response.Headers.Location.ToString());
        #pragma warning restore CS8602
    }

    private async Task<HttpStatusCode> PushAsync(string id, string version, string? apiKey)
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, apiKey);
        using var response = await _client.SendAsync(request);

        return response.StatusCode;
    }

    private async Task<HttpStatusCode> DeleteAsync(string id, string version, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/v2/package/{id}/{version}");
        request.Headers.Add("X-NuGet-ApiKey", apiKey);
        using var response = await _client.SendAsync(request);

        return response.StatusCode;
    }

    private async Task<BaGetUser> CreateUserAsync(string name)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var user = new BaGetUser { UserName = name };

        var result = await users.CreateAsync(user, "Passw0rd!");
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));

        return user;
    }

    private Task<string> CreateKeyAsync(BaGetUser user, string scope, string glob = "*", DateTime? expires = null)
        => WithServiceAsync<ApiKeyService, string>(
            s => s.CreateAsync(user.Id, "test", new[] { scope }, glob, expires, CancellationToken.None));

    private async Task<TResult> WithServiceAsync<TService, TResult>(Func<TService, Task<TResult>> action)
    {
        using var scope = _app.Services.CreateScope();

        #pragma warning disable CS8714
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
        #pragma warning restore CS8714
    }

    private async Task<string> GetFormAsync(string url)
    {
        using var response = await _browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    private async Task<HttpResponseMessage> PostFormAsync(string url, string token, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = token;

        return await _browser.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    public void Dispose()
    {
        _app.Dispose();
        _client.Dispose();
        _browser.Dispose();
    }
}