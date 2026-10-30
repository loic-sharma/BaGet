using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
/// Accounts enabled with no global API key configured. Enabling accounts is asking for
/// ownership to decide who may push what, so an unset global key must mean "there is no
/// global key" - not "every caller is the superuser", which would bypass ownership
/// entirely.
/// </summary>
public class AccountsWithoutGlobalKeyTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public AccountsWithoutGlobalKeyTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            // As src/BaGet/appsettings.json ships it, with accounts turned on.
            { "ApiKey", "" },
            { "Accounts:Enabled", "true" },
        });
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task APushWithNoApiKeyIsRefused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Anonymous.Package", "1.0.0", apiKey: null));
    }

    [Fact]
    public async Task APushWithAnUnknownApiKeyIsRefused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Anonymous.Package", "1.0.0", "not-a-key"));
    }

    [Fact]
    public async Task AUsersOwnKeyStillPushes()
    {
        var alice = await CreateUserAsync("alice");

        Assert.Equal(
            HttpStatusCode.Created,
            await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push)));
    }

    [Fact]
    public async Task OwnershipStillGatesAnIdSomeoneElseOwns()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");

        Assert.Equal(
            HttpStatusCode.Created,
            await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push)));

        // Bob's own key is refused, and so is no key at all: without the second check a
        // keyless push would be the superuser and would overwrite Alice's ID.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            await PushAsync("Alice.Package", "2.0.0", await CreateKeyAsync(bob, ApiKeyScopes.Push)));
        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Alice.Package", "3.0.0", apiKey: null));

        Assert.Equal(new[] { "alice" }, await OwnersAsync("Alice.Package"));
    }

    [Fact]
    public async Task ADeleteWithNoApiKeyIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await CreateUserAsync("alice");
        await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));

        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/v2/package/Alice.Package/1.0.0");
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpStatusCode> PushAsync(string id, string version, string? apiKey)
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, apiKey);
        using var response = await _client.SendAsync(request);

        return response.StatusCode;
    }

    private async Task<IReadOnlyList<string>?> OwnersAsync(string? id)
    {
        using var scope = _app.Services.CreateScope();
        var ownership = scope.ServiceProvider.GetRequiredService<PackageOwnershipService>();

        var owners = await ownership.GetOwnersAsync(id, CancellationToken.None);
        var names = new List<string?>();
        foreach (var owner in owners) names.Add(owner.OwnerName);

        #pragma warning disable CS8619
        return names;
        #pragma warning restore CS8619
    }

    private async Task<BaGetUser> CreateUserAsync(string userName)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();

        var user = new BaGetUser { UserName = userName, Created = DateTime.UtcNow };
        var result = await users.CreateAsync(user, "Passw0rd!");

        Assert.True(result.Succeeded, string.Join(", ", result.Errors));

        return user;
    }

    private async Task<string> CreateKeyAsync(BaGetUser user, string scope)
    {
        using var serviceScope = _app.Services.CreateScope();
        var keys = serviceScope.ServiceProvider.GetRequiredService<ApiKeyService>();

        return await keys.CreateAsync(user.Id, "test", new[] { scope }, "*", expires: null, CancellationToken.None);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}