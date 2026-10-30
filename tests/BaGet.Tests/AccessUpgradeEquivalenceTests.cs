using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Database.Sqlite;
using BaGet.Database.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// B3, the merge gate of the access model: an existing install (a database from before repositories, migrated by the
/// current code, so through <c>AddAccessControl</c>) behaves exactly as before, with no edit to the seeded model:
/// anonymous reads everywhere, pushes and management decided by ownership and key scopes, administrators bypassing
/// ownership. The rest of the authorization suite (<see cref="OwnershipAuthorizationServiceTests"/>, the push, unlist,
/// delete and Manage page tests) runs on the same seeded model, since the test harness migrates a fresh database.
/// </summary>
public class AccessUpgradeEquivalenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BaGetAccessUpgrade", Guid.NewGuid().ToString("N"));
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public AccessUpgradeEquivalenceTests(ITestOutputHelper output)
    {
        Directory.CreateDirectory(_directory);
        var connection = "Data Source=" + Path.Combine(_directory, "old.db");

        PreRepositoryDatabase.CreateAsync(
            new SqliteDatabaseContextFactory(), connection, new DatabaseOptions { Type = "Sqlite" }, CancellationToken.None)
            .GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Database:ConnectionString", connection },
            { "Storage:Path", Path.Combine(_directory, "store") },
            { "Accounts:Enabled", "true" },
            { "ApiKey", "global-key" },
        });
        _client = _app.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private async Task<HttpStatusCode> SendAsync(HttpMethod method, string path, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (key != null) request.Headers.Add("X-NuGet-ApiKey", key);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private async Task<HttpStatusCode> PushAsync(string id, string version, string? key, string prefix = "")
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, key);
        request.RequestUri = new Uri(prefix + "api/v2/package", UriKind.Relative);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>A key of alice (owner of <c>Legacy.Package</c>) that may also unlist, and an administrator's key.</summary>
    private async Task<(string Alice, string Admin)> KeysAsync()
    {
        await AdminTestSupport.CreateUserAsync(_app, "root", administrator: true);
        using var scope = _app.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var all = new[] { ApiKeyScopes.Push, ApiKeyScopes.PushVersion, ApiKeyScopes.Unlist };
        var root = await AdminTestSupport.FindAsync(_app, "root");
        var alice = await AdminTestSupport.FindAsync(_app, PreRepositoryDatabase.UserName);

        return (
            await keys.CreateAsync(alice!.Id, "unlist", all, "*", null, CancellationToken.None),
            await keys.CreateAsync(root!.Id, "admin", all, "*", null, CancellationToken.None));
    }

    [Fact]
    public async Task TheSeededModelIsTheBuiltInGroupsAndTargetsOfTheUpgradeDefaults()
    {
        using var scope = _app.Services.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService<IContext>();
        var token = TestContext.Current.CancellationToken;

        var groups = await context.Database.SqlQueryRaw<string>("SELECT \"Name\" AS \"Value\" FROM \"Groups\" ORDER BY \"Name\"").ToListAsync(token);
        Assert.Equal(new[] { "Authenticated", "Everyone" }, groups);

        var targets = await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"PermissionTargets\"").ToListAsync(token);
        Assert.Equal(new[] { 2 }, targets);

        var grants = await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"PermissionGrants\"").ToListAsync(token);
        Assert.Equal(new[] { 2 }, grants);
    }

    [Fact]
    public async Task AnonymousCallersStillReadEveryRepositoryAndStillCannotWrite()
    {
        var token = TestContext.Current.CancellationToken;
        await _app.CreateRepositoryAsync("second", token);

        foreach (var prefix in new[] { "", "r/second/" })
        {
            Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, prefix + "v3/index.json"));
            Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, prefix + "v3/search?q="));
            Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, prefix + "v3/catalog/index.json"));
            Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, prefix + "api/v2/Packages()"));
            Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Anon.Package", "1.0.0", key: null, prefix));
            Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(HttpMethod.Delete, prefix + "api/v2/package/Legacy.Package/1.0.0"));
        }

        Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, "v3/package/legacy.package/index.json"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, "v3/registration/legacy.package/index.json"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, "packages/Legacy.Package"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, "r/second/"));
    }

    [Fact]
    public async Task OwnershipAndKeyScopesStillDecideWhoMayPushAndManage()
    {
        var (alice, admin) = await KeysAsync();

        // A new ID is claimed by its pusher; an existing one only by an owner.
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Alice.Own", "1.0.0", alice));
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Legacy.Package", "3.0.0", alice));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("Other.Package", "2.0.0", alice));
        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Legacy.Package", "4.0.0", "bgt_not-a-key"));

        // Unlist and delete follow ownership too.
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Delete, "api/v2/package/Legacy.Package/3.0.0", alice));
        Assert.Equal(HttpStatusCode.Forbidden, await SendAsync(HttpMethod.Delete, "api/v2/package/Other.Package/1.0.0", alice));

        // An administrator bypasses ownership; the global key bypasses everything.
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Other.Package", "2.0.0", admin));
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Delete, "api/v2/package/Other.Package/2.0.0", admin));
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Other.Package", "3.0.0", "global-key"));
    }

    [Fact]
    public async Task ASecondRepositoryIsPushedToUnderTheSameRules()
    {
        var (alice, _) = await KeysAsync();
        await _app.CreateRepositoryAsync("second", TestContext.Current.CancellationToken);

        // Ownership is per repository: alice owns Legacy.Package in default only, so in second the ID is new to her.
        Assert.Equal(HttpStatusCode.Created, await PushAsync("Legacy.Package", "1.0.0", alice, "r/second/"));
        Assert.Equal(HttpStatusCode.Unauthorized, await PushAsync("Legacy.Package", "2.0.0", key: null, "r/second/"));
    }
}
