using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// A signed-in browser session carries every scope, so the UI can do what the user's own
/// API key could - and no more: ownership still decides which IDs it may touch.
/// </summary>
public class SessionPrincipalTests : IDisposable
{
    private const string GlobalKey = "global-key";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public SessionPrincipalTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", GlobalKey },
            { "Accounts:Enabled", "true" },
        });
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task ASessionCarriesEveryScopeButIsNotTheSuperuser()
    {
        var alice = await CreateUserAsync("alice");

        var principal = await SessionPrincipalAsync(alice);

        Assert.Equal(alice.Id, principal.UserId);
        Assert.Equal("alice", principal.UserName);
        Assert.False(principal.IsSuperuser);
        Assert.Equal(ApiKeyScopes.All.OrderBy(s => s), principal.Scopes.OrderBy(s => s));
        Assert.True(principal.MatchesPackage("Anything.At.All"));
    }

    [Fact]
    public async Task OnlyAnAdministratorsSessionIsAnAdministrator()
    {
        var alice = await CreateUserAsync("alice");
        Assert.False((await SessionPrincipalAsync(alice)).IsAdministrator);

        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var stored = await users.FindByIdAsync(alice.Id);
            #pragma warning disable CS8602
            stored.IsAdministrator = true;
            #pragma warning restore CS8602
            Assert.True((await users.UpdateAsync(stored)).Succeeded);
        }

        var principal = await SessionPrincipalAsync(alice);
        Assert.True(principal.IsAdministrator);
        Assert.False(principal.IsSuperuser);
    }

    [Fact]
    public async Task AnAnonymousSessionHasNoPrincipal()
    {
        using var scope = _app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<SessionPrincipalFactory>();

        Assert.Null(await factory.CreateAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(await factory.CreateAsync(null));
    }

    [Fact]
    public async Task ASessionCanPushANewIdAndItsOwnPackages()
    {
        var alice = await CreateUserAsync("alice");
        await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));

        var principal = await SessionPrincipalAsync(alice);

        Assert.True(await AuthorizeAsync(principal, PackageAction.Push, "Brand.New.Id"));
        Assert.True(await AuthorizeAsync(principal, PackageAction.Push, "Alice.Package"));
        Assert.True(await AuthorizeAsync(principal, PackageAction.Manage, "Alice.Package"));
    }

    [Fact]
    public async Task ASessionCannotPushAnIdOwnedByAnotherUser()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");
        await PushAsync("Alice.Package", "1.0.0", await CreateKeyAsync(alice, ApiKeyScopes.Push));

        var principal = await SessionPrincipalAsync(bob);

        // Bob's session is as privileged as any of his keys, which is to say: not on
        // Alice's package.
        Assert.False(await AuthorizeAsync(principal, PackageAction.Push, "Alice.Package"));
        Assert.False(await AuthorizeAsync(principal, PackageAction.Manage, "Alice.Package"));

        // The same session on his own ID, so the refusal above is ownership and not an
        // unusable principal.
        Assert.True(await AuthorizeAsync(principal, PackageAction.Push, "Bob.Package"));
    }

    [Fact]
    public async Task ASessionCannotPushAnIdThatPredatesAccounts()
    {
        await PushAsync("Legacy.Package", "1.0.0", GlobalKey);
        var alice = await CreateUserAsync("alice");

        Assert.False(await AuthorizeAsync(await SessionPrincipalAsync(alice), PackageAction.Push, "Legacy.Package"));
    }

    private async Task<BaGetPrincipal> SessionPrincipalAsync(BaGetUser user)
    {
        using var scope = _app.Services.CreateScope();

        // The claims the sign-in cookie would carry, built the way sign-in builds them.
        var claims = await scope.ServiceProvider
            .GetRequiredService<IUserClaimsPrincipalFactory<BaGetUser>>()
            .CreateAsync(user);

        var principal = await scope.ServiceProvider
            .GetRequiredService<SessionPrincipalFactory>()
            .CreateAsync(claims);

        Assert.NotNull(principal);

        return principal;
    }

    private async Task<bool> AuthorizeAsync(BaGetPrincipal? principal, PackageAction action, string? packageId)
    {
        using var scope = _app.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IPackageAuthorizationService>();

        return await authorization.AuthorizeAsync(principal, action, packageId, CancellationToken.None);
    }

    private async Task<HttpStatusCode> PushAsync(string id, string version, string? apiKey)
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, apiKey);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

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

    private async Task<string> CreateKeyAsync(BaGetUser user, string scopeName)
    {
        using var scope = _app.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();

        return await keys.CreateAsync(user.Id, "test", new[] { scopeName }, "*", null, CancellationToken.None);
    }

    public void Dispose()
    {
        _app.Dispose();
        _client.Dispose();
    }
}