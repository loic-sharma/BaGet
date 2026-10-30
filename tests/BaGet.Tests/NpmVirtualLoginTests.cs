using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
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
/// <c>npm login</c> on a private virtual repository: the credential call is not a write to the repository's content, so it is
/// answered by the virtual repository itself (not moved to its deploy repository), and the token it issues reads through the
/// virtual repository and pushes to its deploy repository.
/// </summary>
public class NpmVirtualLoginTests : IDisposable
{
    private const string Registry = "/r/all/npm/";
    private const string Password = "Passw0rd!";
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public NpmVirtualLoginTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "ApiKey", "global-key" }, { "Accounts:Enabled", "true" } });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        _app.CreateNpmRepositoryAsync("local1", anonymousRead: false, cancellationToken: Cancel).GetAwaiter().GetResult();
        CreateUserAsync(_app, "alice").GetAwaiter().GetResult();
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
            "all",
            new VirtualRepositorySettings { Members = new List<string> { "local1" }, DeployRepository = "local1" },
            Cancel,
            actor: null,
            anonymousRead: false,
            RepositoryFormat.Npm).GetAwaiter().GetResult();

        // The repositories are private: Alice reads and deploys by a grant, as in any private repository.
        var access = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
        access.CreateTargetAsync("alice rw", new[] { "all", "local1" }, null, null, null, null, Cancel).GetAwaiter().GetResult();
        access.SetGrantAsync("alice rw", PrincipalType.User, FindAsync(_app, "alice").GetAwaiter().GetResult()!.Id, AccessAction.Read | AccessAction.Deploy, null, Cancel).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string? body = null, string? token = null)
    {
        using var request = NpmTestPackages.Json(method, uri, body, token);

        return await _client.SendAsync(request, Cancel);
    }

    [Fact]
    public async Task ATokenFromALoginOnAVirtualRepositoryReadsAndPushesThroughIt()
    {
        using var login = await SendAsync(
            HttpMethod.Put,
            Registry + "-/user/org.couchdb.user:alice",
            new JsonObject { ["_id"] = "org.couchdb.user:alice", ["name"] = "alice", ["password"] = Password, ["type"] = "user" }.ToJsonString());
        Assert.Equal(HttpStatusCode.Created, login.StatusCode);
        var token = (string)JsonNode.Parse(await login.Content.ReadAsStringAsync(Cancel))!["token"]!;

        using var anonymous = await SendAsync(HttpMethod.Get, Registry + "-/whoami");
        using var whoami = await SendAsync(HttpMethod.Get, Registry + "-/whoami", token: token);
        using var publish = await SendAsync(HttpMethod.Put, NpmTestPackages.Path(Registry, "left-pad"), NpmTestPackages.PublishBody(NpmTestPackages.Tarball("left-pad", "1.0.0")), token);
        using var install = await SendAsync(HttpMethod.Get, NpmTestPackages.Path(Registry, "left-pad"), token: token);
        using var direct = await SendAsync(HttpMethod.Get, NpmTestPackages.Path("/r/local1/npm/", "left-pad"), token: token);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, whoami.StatusCode);
        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, install.StatusCode);
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
    }

    [Fact]
    public async Task TheLoginIsNotMovedToTheDeployRepository()
    {
        using var login = await SendAsync(
            HttpMethod.Put,
            Registry + "-/user/org.couchdb.user:alice",
            new JsonObject { ["_id"] = "org.couchdb.user:alice", ["name"] = "alice", ["password"] = Password, ["type"] = "user" }.ToJsonString());
        Assert.Equal(HttpStatusCode.Created, login.StatusCode);

        using var scope = _app.Services.CreateScope();
        var keys = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync((await FindAsync(_app, "alice"))!.Id, Cancel);
        var key = Assert.Single(keys);
        var limit = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().RepositoryKeysAsync(key.Key, Cancel);
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
        var all = (await repositories.FindAsync("all", Cancel))!.Key;
        var local = (await repositories.FindAsync("local1", Cancel))!.Key;

        // The key was issued for the virtual repository (and what it answers with), not for the deploy repository alone.
        Assert.Equal("npm login (all)", key.Description);
        Assert.Contains(all, limit!);
        Assert.Contains(local, limit!);
    }
}
