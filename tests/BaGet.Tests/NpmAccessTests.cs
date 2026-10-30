using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// Who may do what in an npm repository of a server with accounts: package ownership (the first publisher of a name owns it),
/// the access model's actions (Read, Deploy, Annotate, Delete), the scopes of an API key, the unpublish window, private
/// repositories, and <c>npm login</c> / <c>logout</c> / <c>whoami</c>.
/// </summary>
public class NpmAccessTests : IDisposable
{
    private const string Registry = "/r/web/npm/";

    private readonly ITestOutputHelper _output;
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public NpmAccessTests(ITestOutputHelper output)
        : this(output, new Dictionary<string, string>())
    {
    }

    private NpmAccessTests(ITestOutputHelper output, Dictionary<string, string> extra)
    {
        _output = output;
        var configuration = new Dictionary<string, string> { { "ApiKey", "global-key" }, { "Accounts:Enabled", "true" } };
        foreach (var pair in extra) configuration[pair.Key] = pair.Value;

        _app = new BaGetApplication(output, configOverrides: configuration);
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
        await _app.CreateNpmRepositoryAsync("web", cancellationToken: CancellationToken.None);
        await _app.CreateNpmRepositoryAsync("other", cancellationToken: CancellationToken.None);
        foreach (var name in new[] { "alice", "bob" }) await CreateUserAsync(_app, name);
        await CreateUserAsync(_app, "root", administrator: true);
    }

    private async Task<string> KeyAsync(string user, string[]? scopes = null, string? repository = null)
    {
        using var scope = _app.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var limit = repository == null
            ? null
            : new[] { (await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!.Key };

        return await keys.CreateAsync((await FindAsync(_app, user))!.Id, "test", scopes ?? ApiKeyScopes.All, "*", null, limit, CancellationToken.None);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string? body = null, string? token = null)
    {
        using var request = NpmTestPackages.Json(method, uri, body, token);

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpStatusCode> PublishAsync(string token, string name, string version, string tag = "latest", string registry = Registry)
    {
        using var response = await SendAsync(HttpMethod.Put, NpmTestPackages.Path(registry, name), NpmTestPackages.PublishBody(NpmTestPackages.Tarball(name, version), tag), token);

        return response.StatusCode;
    }

    private async Task<IReadOnlyList<PackageOwner>> OwnersAsync(string name)
        => await WithAsync(_app, async services =>
        {
            services.GetRequiredService<IRepositoryContext>().Use((await services.GetRequiredService<IRepositoryService>().FindAsync("web", CancellationToken.None))!);

            return await services.GetRequiredService<PackageOwnershipService>().GetOwnersAsync(name, CancellationToken.None);
        });

    private async Task WithAccessAsync(Func<IAccessAdministration, Task> change)
    {
        using var scope = _app.Services.CreateScope();
        await change(scope.ServiceProvider.GetRequiredService<IAccessAdministration>());
    }

    // ---- ownership ----

    [Fact]
    public async Task TheFirstPublisherOfANameOwnsItAndOnlyOwnersAndAdministratorsPublishVersionsOfIt()
    {
        var alice = await KeyAsync("alice");
        var bob = await KeyAsync("bob");
        var root = await KeyAsync("root");

        Assert.Equal(HttpStatusCode.Created, await PublishAsync(alice, "left-pad", "1.0.0"));
        Assert.Equal("alice", Assert.Single(await OwnersAsync("left-pad")).OwnerName);

        Assert.Equal(HttpStatusCode.Forbidden, await PublishAsync(bob, "left-pad", "1.1.0"));
        Assert.Equal(HttpStatusCode.Created, await PublishAsync(alice, "left-pad", "1.1.0"));
        Assert.Equal(HttpStatusCode.Created, await PublishAsync(root, "left-pad", "1.2.0"));
        // An administrator's push does not take over the name.
        Assert.Equal("alice", Assert.Single(await OwnersAsync("left-pad")).OwnerName);

        Assert.Equal(HttpStatusCode.Created, await PublishAsync(bob, "bobs-lib", "1.0.0"));
        Assert.Equal("bob", Assert.Single(await OwnersAsync("bobs-lib")).OwnerName);
    }

    [Fact]
    public async Task TagsDeprecationAndUnpublishingBelongToTheOwner()
    {
        var alice = await KeyAsync("alice");
        var bob = await KeyAsync("bob");
        await PublishAsync(alice, "left-pad", "1.0.0");
        await PublishAsync(alice, "left-pad", "1.1.0");

        using var tagByBob = await SendAsync(HttpMethod.Put, Registry + "-/package/left-pad/dist-tags/stable", "\"1.0.0\"", bob);
        using var untagByBob = await SendAsync(HttpMethod.Delete, Registry + "-/package/left-pad/dist-tags/latest", token: bob);
        using var deleteByBob = await SendAsync(HttpMethod.Delete, Registry + "left-pad/-rev/1-x", token: bob);
        using var fileByBob = await SendAsync(HttpMethod.Delete, Registry + "left-pad/-/left-pad-1.1.0.tgz/-rev/1-x", token: bob);

        Assert.Equal(HttpStatusCode.Forbidden, tagByBob.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, untagByBob.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deleteByBob.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, fileByBob.StatusCode);

        using var current = await SendAsync(HttpMethod.Get, NpmTestPackages.Path(Registry, "left-pad"));
        var packument = JsonNode.Parse(await current.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        packument["versions"]!["1.0.0"]!["deprecated"] = "no";
        using var deprecateByBob = await SendAsync(HttpMethod.Put, NpmTestPackages.Path(Registry, "left-pad"), packument.ToJsonString(), bob);
        Assert.Equal(HttpStatusCode.Forbidden, deprecateByBob.StatusCode);
        using var deprecateByAlice = await SendAsync(HttpMethod.Put, NpmTestPackages.Path(Registry, "left-pad"), packument.ToJsonString(), alice);
        Assert.Equal(HttpStatusCode.OK, deprecateByAlice.StatusCode);
    }

    [Fact]
    public async Task APackageNameThatExistedBeforeAccountsHasNoOwnerAndOnlyTheGlobalKeyChangesIt()
    {
        using (var global = await SendAsync(HttpMethod.Put, NpmTestPackages.Path(Registry, "legacy"), NpmTestPackages.PublishBody(NpmTestPackages.Tarball("legacy", "1.0.0")), "global-key"))
        {
            Assert.Equal(HttpStatusCode.Created, global.StatusCode);
        }

        var alice = await KeyAsync("alice");

        Assert.Empty(await OwnersAsync("legacy"));
        Assert.Equal(HttpStatusCode.Forbidden, await PublishAsync(alice, "legacy", "1.1.0"));
    }

    [Fact]
    public async Task AnApiKeyActsOnlyWithinItsScopes()
    {
        var versionOnly = await KeyAsync("alice", new[] { ApiKeyScopes.PushVersion });
        var pushOnly = await KeyAsync("alice", new[] { ApiKeyScopes.Push });
        var full = await KeyAsync("alice");

        // A key that may push versions cannot claim a new name.
        Assert.Equal(HttpStatusCode.Forbidden, await PublishAsync(versionOnly, "left-pad", "1.0.0"));
        Assert.Equal(HttpStatusCode.Created, await PublishAsync(pushOnly, "left-pad", "1.0.0"));
        Assert.Equal(HttpStatusCode.Created, await PublishAsync(versionOnly, "left-pad", "1.1.0"));

        // Deprecating and deleting need the unlist scope.
        using var deprecate = await SendAsync(HttpMethod.Get, NpmTestPackages.Path(Registry, "left-pad"));
        var packument = JsonNode.Parse(await deprecate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        packument["versions"]!["1.0.0"]!["deprecated"] = "no";
        using var withoutUnlist = await SendAsync(HttpMethod.Put, NpmTestPackages.Path(Registry, "left-pad"), packument.ToJsonString(), pushOnly);
        using var withUnlist = await SendAsync(HttpMethod.Put, NpmTestPackages.Path(Registry, "left-pad"), packument.ToJsonString(), full);

        Assert.Equal(HttpStatusCode.Forbidden, withoutUnlist.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withUnlist.StatusCode);
    }

    [Fact]
    public async Task AKeyLimitedToOneRepositoryIsRefusedInAnother()
    {
        var onlyWeb = await KeyAsync("alice", repository: "web");

        Assert.Equal(HttpStatusCode.Created, await PublishAsync(onlyWeb, "left-pad", "1.0.0"));
        Assert.Equal(HttpStatusCode.Forbidden, await PublishAsync(onlyWeb, "left-pad", "1.0.0", registry: "/r/other/npm/"));
    }

    // ---- unpublish window ----

    [Fact]
    public async Task ADeployOnlyOwnerMayUnpublishAYoungVersion()
    {
        var alice = await KeyAsync("alice");
        await PublishAsync(alice, "left-pad", "1.0.0");
        await PublishAsync(alice, "left-pad", "1.1.0");
        // Alice may deploy but not delete.
        await WithAccessAsync(async admin =>
        {
            await admin.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, CancellationToken.None);
            await admin.CreateTargetAsync("alice deploys", new[] { "web" }, null, null, null, null, CancellationToken.None);
            await admin.SetGrantAsync("alice deploys", PrincipalType.User, (await FindAsync(_app, "alice"))!.Id, AccessAction.Read | AccessAction.Deploy, null, CancellationToken.None);
        });

        using var young = await SendAsync(HttpMethod.Delete, Registry + "left-pad/-/left-pad-1.1.0.tgz/-rev/1-x", token: alice);
        Assert.Equal(HttpStatusCode.OK, young.StatusCode);
    }

    [Fact]
    public async Task AKeyThatOnlyPushesVersionsNeverUnpublishesEvenAYoungVersion()
    {
        var full = await KeyAsync("alice");
        var pushVersion = await KeyAsync("alice", new[] { ApiKeyScopes.PushVersion });
        await PublishAsync(full, "left-pad", "1.0.0");
        await PublishAsync(pushVersion, "left-pad", "1.1.0");

        using var version = await SendAsync(HttpMethod.Delete, Registry + "left-pad/-/left-pad-1.1.0.tgz/-rev/1-x", token: pushVersion);
        using var whole = await SendAsync(HttpMethod.Delete, Registry + "left-pad/-rev/1-x", token: pushVersion);
        using var packument = await SendAsync(HttpMethod.Get, Registry + "left-pad");

        Assert.Equal(HttpStatusCode.Forbidden, version.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, whole.StatusCode);
        Assert.Equal(HttpStatusCode.OK, packument.StatusCode);
        Assert.Contains("1.1.0", await packument.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithoutAWindowUnpublishingNeedsTheDeleteAction()
    {
        using var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", "global-key" }, { "Accounts:Enabled", "true" }, { "Npm:UnpublishWindow", "00:00:00" } });
        await app.CreateNpmRepositoryAsync("web", cancellationToken: CancellationToken.None);
        await CreateUserAsync(app, "alice");
        using var client = app.CreateClient();
        string key;
        using (var scope = app.Services.CreateScope())
        {
            key = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
                .CreateAsync((await FindAsync(app, "alice"))!.Id, "t", ApiKeyScopes.All, "*", null, CancellationToken.None);
        }

        async Task<HttpStatusCode> DeleteAsync(string version)
        {
            using var request = NpmTestPackages.Json(HttpMethod.Delete, Registry + $"left-pad/-/left-pad-{version}.tgz/-rev/1-x", null, key);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            return response.StatusCode;
        }

        foreach (var version in new[] { "1.0.0", "1.1.0", "1.2.0" })
        {
            using var request = NpmTestPackages.Json(HttpMethod.Put, NpmTestPackages.Path(Registry, "left-pad"), NpmTestPackages.PublishBody(NpmTestPackages.Tarball("left-pad", version)), key);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        // The seeded target gives every signed-in user Delete: the version goes.
        Assert.Equal(HttpStatusCode.OK, await DeleteAsync("1.2.0"));

        // Take Delete away (keep Read and Deploy): the same call is now refused.
        using (var scope = app.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await admin.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, CancellationToken.None);
            await admin.CreateTargetAsync("alice deploys", new[] { "web" }, null, null, null, null, CancellationToken.None);
            await admin.SetGrantAsync("alice deploys", PrincipalType.User, (await FindAsync(app, "alice"))!.Id, AccessAction.Read | AccessAction.Deploy, null, CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.Forbidden, await DeleteAsync("1.1.0"));

        using (var scope = app.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await admin.SetGrantAsync("alice deploys", PrincipalType.User, (await FindAsync(app, "alice"))!.Id, AccessAction.Read | AccessAction.Deploy | AccessAction.Delete, null, CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.OK, await DeleteAsync("1.1.0"));
    }

    // ---- private repositories ----

    [Fact]
    public async Task APrivateRepositoryChallengesWithBearerAndServesReadersOnly()
    {
        var alice = await KeyAsync("alice");
        var bob = await KeyAsync("bob");
        await PublishAsync(alice, "left-pad", "1.0.0");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("web", false, CancellationToken.None);
            var admin = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await admin.CreateTargetAsync("alice reads web", new[] { "web" }, null, null, null, null, CancellationToken.None);
            await admin.SetGrantAsync("alice reads web", PrincipalType.User, (await FindAsync(_app, "alice"))!.Id, AccessAction.Read, null, CancellationToken.None);
        }

        foreach (var path in new[] { "left-pad", "left-pad/-/left-pad-1.0.0.tgz", "-/v1/search?text=a", "-/package/left-pad/dist-tags", "-/ping", "-/whoami" })
        {
            using var anonymous = await SendAsync(HttpMethod.Get, Registry + path);
            Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized, path + " " + anonymous.StatusCode);
            Assert.Contains(anonymous.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");

            using var reader = await SendAsync(HttpMethod.Get, Registry + path, token: alice);
            Assert.True(reader.StatusCode == HttpStatusCode.OK, path + " " + reader.StatusCode);

            using var stranger = await SendAsync(HttpMethod.Get, Registry + path, token: bob);
            Assert.True(stranger.StatusCode == HttpStatusCode.NotFound, path + " " + stranger.StatusCode);
        }

        // The audit POSTs read nothing, but they are still behind the repository's Read check.
        using var audit = await SendAsync(HttpMethod.Post, Registry + "-/npm/v1/security/advisories/bulk", "{}");
        Assert.Equal(HttpStatusCode.Unauthorized, audit.StatusCode);
    }

    [Fact]
    public async Task WritesByACallerWhoCannotReadAnswerTheSameForAnExistingAndAMissingPackage()
    {
        var alice = await KeyAsync("alice");
        var bob = await KeyAsync("bob");
        await PublishAsync(alice, "left-pad", "1.0.0");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("web", false, CancellationToken.None);
        }

        // bob is authenticated but has no Read here and does not own left-pad: nothing he does may tell the two names apart.
        var requests = new (HttpMethod Method, string Path, string? Body)[]
        {
            (HttpMethod.Put, "-/package/{0}/dist-tags/beta", "\"1.0.0\""),
            (HttpMethod.Delete, "-/package/{0}/dist-tags/latest", null),
            (HttpMethod.Put, "{0}", """{"name":"{0}","versions":{"1.0.0":{"deprecated":"nope"}}}"""),
            (HttpMethod.Put, "{0}/-rev/1-x", """{"name":"{0}","versions":{}}"""),
            (HttpMethod.Delete, "{0}/-rev/1-x", null),
        };

        foreach (var (method, path, body) in requests)
        {
            using var existing = await SendAsync(method, Registry + string.Format(path.Replace("{0}", "{0}"), "left-pad"), body?.Replace("{0}", "left-pad"), bob);
            using var missing = await SendAsync(method, Registry + string.Format(path, "nothing-here"), body?.Replace("{0}", "nothing-here"), bob);

            var label = method + " " + path;
            Assert.True(existing.StatusCode == HttpStatusCode.NotFound, label + " existing: " + existing.StatusCode);
            Assert.True(missing.StatusCode == HttpStatusCode.NotFound, label + " missing: " + missing.StatusCode);
            Assert.Equal(await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), await existing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // Deleting the tarball of a published version is hidden too. (A tarball of a package that has no version left is an
        // idempotent 200 for anyone who may deploy that name, as npm's unpublish sequence needs; the name is claimable anyway.)
        using var tarball = await SendAsync(HttpMethod.Delete, Registry + "left-pad/-/left-pad-1.0.0.tgz/-rev/1-x", token: bob);
        Assert.Equal(HttpStatusCode.NotFound, tarball.StatusCode);

        // The package is untouched.
        using var still = await SendAsync(HttpMethod.Get, Registry + "left-pad", token: await KeyAsync("root"));
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task APackagePatternOfAGrantHidesTheOtherPackagesWhateverTheSpellingOfTheName()
    {
        var alice = await KeyAsync("alice");
        await PublishAsync(alice, "@acme/secret", "1.0.0");
        await PublishAsync(alice, "left-pad", "1.0.0");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("web", false, CancellationToken.None);
            var admin = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            var bob = (await FindAsync(_app, "bob"))!;
            await admin.CreateTargetAsync("bob reads left", new[] { "web" }, new[] { "left-*" }, null, null, null, CancellationToken.None);
            await admin.SetGrantAsync("bob reads left", PrincipalType.User, bob.Id, AccessAction.Read, null, CancellationToken.None);
        }

        var bobKey = await KeyAsync("bob");
        using var encoded = await SendAsync(HttpMethod.Get, Registry + "@acme%2fsecret", token: bobKey);
        using var plain = await SendAsync(HttpMethod.Get, Registry + "@acme/secret", token: bobKey);
        using var tarball = await SendAsync(HttpMethod.Get, Registry + "@acme/secret/-/secret-1.0.0.tgz", token: bobKey);
        using var allowed = await SendAsync(HttpMethod.Get, Registry + "left-pad", token: bobKey);

        Assert.Equal(HttpStatusCode.NotFound, encoded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, plain.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tarball.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task ADeployOnlyDropBoxCanPublishWithoutRead()
    {
        var alice = await KeyAsync("alice");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("web", false, CancellationToken.None);
            var admin = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await admin.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, null, CancellationToken.None);
            await admin.CreateTargetAsync("drop box", new[] { "web" }, null, null, null, null, CancellationToken.None);
            await admin.SetGrantAsync("drop box", PrincipalType.User, (await FindAsync(_app, "alice"))!.Id, AccessAction.Deploy, null, CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.Created, await PublishAsync(alice, "left-pad", "1.0.0"));
        using var read = await SendAsync(HttpMethod.Get, Registry + "left-pad", token: alice);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    // ---- login ----

    private Task<HttpResponseMessage> LoginAsync(string user, string password, string registry = Registry)
        => SendAsync(HttpMethod.Put, registry + "-/user/org.couchdb.user:" + user, new JsonObject { ["_id"] = "org.couchdb.user:" + user, ["name"] = user, ["password"] = password, ["type"] = "user" }.ToJsonString());

    [Fact]
    public async Task LoggingInAnswersATokenThatActsAsTheUserAndOnlyInThatRepository()
    {
        using var login = await LoginAsync("alice", Password);
        Assert.Equal(HttpStatusCode.Created, login.StatusCode);
        var body = JsonNode.Parse(await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(true, (bool?)body["ok"]);
        Assert.Equal("org.couchdb.user:alice", (string?)body["id"]);
        var token = (string)body["token"]!;
        Assert.StartsWith("bgt_", token);

        using var whoami = await SendAsync(HttpMethod.Get, Registry + "-/whoami", token: token);
        Assert.Equal("alice", (string?)JsonNode.Parse(await whoami.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["username"]);

        Assert.Equal(HttpStatusCode.Created, await PublishAsync(token, "left-pad", "1.0.0"));
        Assert.Equal("alice", Assert.Single(await OwnersAsync("left-pad")).OwnerName);

        // The token was issued for web only.
        Assert.Equal(HttpStatusCode.Forbidden, await PublishAsync(token, "left-pad", "1.0.0", registry: "/r/other/npm/"));

        // It is a key of the account, with a description, so the account's API keys page lists and revokes it.
        using var scope = _app.Services.CreateScope();
        var keys = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync((await FindAsync(_app, "alice"))!.Id, CancellationToken.None);
        Assert.Equal("npm login (web)", Assert.Single(keys).Description);
    }

    [Fact]
    public async Task ALoginBodyWithANonStringFieldIsA400NotA500()
    {
        using var number = await SendAsync(HttpMethod.Put, Registry + "-/user/org.couchdb.user:alice", """{"name":"alice","password":1}""");
        using var flag = await SendAsync(HttpMethod.Put, Registry + "-/user/org.couchdb.user:alice", """{"name":true,"password":"x"}""");

        Assert.Equal(HttpStatusCode.BadRequest, number.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, flag.StatusCode);
    }

    [Fact]
    public async Task ABadPasswordAnUnknownUserOrAMismatchedNameIsRefusedWithoutAToken()
    {
        using var badPassword = await LoginAsync("alice", "wrong-Passw0rd!");
        using var unknown = await LoginAsync("nobody", Password);
        using var mismatch = await SendAsync(HttpMethod.Put, Registry + "-/user/org.couchdb.user:alice", """{"name":"bob","password":"x"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, badPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        // Nobody was created by logging in.
        Assert.Null(await FindAsync(_app, "nobody"));
        Assert.DoesNotContain("token", await badPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RepeatedBadPasswordsLockTheAccountOut()
    {
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 12 && last != HttpStatusCode.TooManyRequests; i++)
        {
            using var response = await LoginAsync("alice", "wrong-Passw0rd!");
            last = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        // Even the right password is refused while it lasts.
        using var correct = await LoginAsync("alice", Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, correct.StatusCode);
    }

    [Fact]
    public async Task AnAccountWithTwoFactorAuthenticationCannotLogInWithAPasswordAlone()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var bob = (await users.FindByNameAsync("bob"))!;
            await users.SetTwoFactorEnabledAsync(bob, true);
        }

        using var response = await LoginAsync("bob", Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("two-factor", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoggingOutRevokesTheToken()
    {
        using var login = await LoginAsync("alice", Password);
        var token = (string)JsonNode.Parse(await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["token"]!;

        using var logout = await SendAsync(HttpMethod.Delete, Registry + "-/user/token/" + token, token: token);
        using var after = await SendAsync(HttpMethod.Get, Registry + "-/whoami", token: token);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task ATokenCannotRevokeAnotherUsersToken()
    {
        using var aliceLogin = await LoginAsync("alice", Password);
        using var bobLogin = await LoginAsync("bob", Password);
        var aliceToken = (string)JsonNode.Parse(await aliceLogin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["token"]!;
        var bobToken = (string)JsonNode.Parse(await bobLogin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["token"]!;

        using var logout = await SendAsync(HttpMethod.Delete, Registry + "-/user/token/" + aliceToken, token: bobToken);
        using var stillThere = await SendAsync(HttpMethod.Get, Registry + "-/whoami", token: aliceToken);

        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task TheBrowserSessionIsNeverAWriteCredential()
    {
        using var browser = new Browser(_app);
        await SignInAsync(browser, "alice");

        // Cookies ride along with any request a web page makes, so they authenticate no npm write.
        var body = NpmTestPackages.PublishBody(NpmTestPackages.Tarball("left-pad", "1.0.0"));
        using var response = await browser.SendAsync(NpmTestPackages.Json(HttpMethod.Put, NpmTestPackages.Path(Registry, "left-pad"), body));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublishingFromTheNpmClientsBearerTokenDoesNotNeedAUserNameMatch()
    {
        var alice = await KeyAsync("alice");
        using var request = NpmTestPackages.Json(HttpMethod.Put, NpmTestPackages.Path(Registry, "left-pad"), NpmTestPackages.PublishBody(NpmTestPackages.Tarball("left-pad", "1.0.0")), alice);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        Assert.Equal(1, await context.NpmVersions!.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchNamesOnlyThePackagesTheCallerMayRead()
    {
        var alice = await KeyAsync("alice");
        await PublishAsync(alice, "@acme/secret", "1.0.0");
        await PublishAsync(alice, "left-pad", "1.0.0");
        await PublishAsync(alice, "left-over", "1.0.0");
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync("web", false, CancellationToken.None);
            var admin = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
            await admin.CreateTargetAsync("bob reads left", new[] { "web" }, new[] { "left-*" }, null, null, null, CancellationToken.None);
            await admin.SetGrantAsync("bob reads left", PrincipalType.User, (await FindAsync(_app, "bob"))!.Id, AccessAction.Read, null, CancellationToken.None);
        }

        var bob = await KeyAsync("bob");
        using var all = await SendAsync(HttpMethod.Get, Registry + "-/v1/search?text=&size=1&from=0", token: bob);
        using var second = await SendAsync(HttpMethod.Get, Registry + "-/v1/search?text=&size=1&from=1", token: bob);
        using var root = await SendAsync(HttpMethod.Get, Registry + "-/v1/search?text=", token: await KeyAsync("root"));

        var first = JsonNode.Parse(await all.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(2, (int?)first["total"]);
        Assert.Equal("left-over", (string?)first["objects"]![0]!["package"]!["name"]);
        Assert.Equal("left-pad", (string?)JsonNode.Parse(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["objects"]![0]!["package"]!["name"]);
        Assert.Equal(3, (int?)JsonNode.Parse(await root.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["total"]);
    }
}
