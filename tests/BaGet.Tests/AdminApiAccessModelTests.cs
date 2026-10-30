using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// <c>/api/v1</c> for the access model and accounts: projects, groups, permission targets and grants, the audit, and users. Each change is
/// audited by the service it calls, asks for the password where its page does, and takes effect in the evaluator.
/// </summary>
public class AdminApiAccessModelTests : IDisposable
{
    private readonly AdminApiHarness _api;

    public AdminApiAccessModelTests(ITestOutputHelper output)
    {
        _api = new AdminApiHarness(output);
    }

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task AGroupIsCreatedFilledRenamedAndDeletedAndThePasswordIsAskedWhereThePageAsksIt()
    {
        var created = await _api.PostAsync("/api/v1/groups", new { name = "devs", description = "Developers" });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(0, created.Json["memberCount"]!.GetValue<int>());

        Assert.Equal("confirmation-required", (await _api.PutAsync("/api/v1/groups/devs/members/alice")).Code);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PutAsync("/api/v1/groups/devs/members/alice", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.PutAsync("/api/v1/groups/devs/members/nobody", password: Password)).Status);

        var group = await _api.GetAsync("/api/v1/groups/devs");
        Assert.Equal("alice", Assert.Single(group.Json["members"]!.AsArray())!["userName"]!.GetValue<string>());

        // Renaming asks for nothing, as its page does.
        var renamed = await _api.SendAsync(HttpMethod.Patch, "/api/v1/groups/devs", new { newName = "engineers" });
        Assert.Equal(HttpStatusCode.OK, renamed.Status);
        Assert.Equal("engineers", renamed.Json["name"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/groups/engineers/members/alice", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/groups/engineers", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/groups/engineers")).Status);

        // Built-in groups stay.
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.DeleteAsync("/api/v1/groups/Everyone", password: Password)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/groups", new { name = "Everyone" })).Status);

        var list = await _api.GetAsync("/api/v1/groups");
        Assert.Contains(list.Json["items"]!.AsArray(), g => g!["name"]!.GetValue<string>() == "Everyone");
    }

    [Fact]
    public async Task AGrantThroughAPermissionTargetChangesWhatTheUserMayDo()
    {
        await _api.App.CreateRepositoryAsync("secret", AdminApiHarness.Token);
        await _api.PatchAnonymousReadOffAsync("secret");
        // The seeded targets give every authenticated user Deploy everywhere: take them away so only the grant below counts.
        foreach (var seeded in new[] { "Anyone%20reads%20every%20repository", "Authenticated%20users%20deploy" })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/permission-targets/" + seeded, password: Password)).Status);
        }

        await _api.PostAsync("/api/v1/groups", new { name = "readers" });
        await _api.PutAsync("/api/v1/groups/readers/members/alice", password: Password);

        var target = await _api.PostAsync("/api/v1/permission-targets", new { name = "read-secret", repositoryPatterns = new[] { "secret" } });
        Assert.Equal(HttpStatusCode.Created, target.Status);

        var access = async () => (await _api.GetAsync("/api/v1/users/alice/access")).Json.AsArray()
            .Single(r => r!["repository"]!.GetValue<string>() == "secret")!["actions"]!.GetValue<string>();
        var before = await access();

        var grant = await _api.PutAsync("/api/v1/permission-targets/read-secret/grants", new { type = "Group", principal = "readers", actions = new[] { "Read", "Deploy" } });
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.PutAsync("/api/v1/permission-targets/read-secret/grants", new { type = "Group", principal = "readers", actions = new[] { "Read" } })).Status);
        grant = await _api.PutAsync("/api/v1/permission-targets/read-secret/grants", new { type = "Group", principal = "readers", actions = new[] { "Read", "Deploy" } }, password: Password);
        Assert.Equal(HttpStatusCode.OK, grant.Status);
        Assert.Equal("Group", Assert.Single(grant.Json["grants"]!.AsArray())!["principalType"]!.GetValue<string>());

        var after = await access();
        Assert.DoesNotContain("Deploy", before);
        Assert.Contains("Deploy", after);

        // None removes it again.
        await _api.PutAsync("/api/v1/permission-targets/read-secret/grants", new { type = "Group", principal = "readers", actions = Array.Empty<string>() }, password: Password);
        Assert.Empty((await _api.GetAsync("/api/v1/permission-targets/read-secret")).Json["grants"]!.AsArray());

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsync("/api/v1/permission-targets/read-secret/grants", new { type = "Group", principal = "readers", actions = new[] { "Fly" } }, password: Password)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsync("/api/v1/permission-targets/read-secret/grants", new { type = "Robot", principal = "x", actions = new[] { "Read" } }, password: Password)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsync("/api/v1/permission-targets/read-secret/grants", new { type = "Group", principal = "ghosts", actions = new[] { "Read" } }, password: Password)).Status);
    }

    [Fact]
    public async Task PermissionTargetsAreUpdatedPreviewedDeletedAndTheBuiltInsRestored()
    {
        await _api.App.CreateRepositoryAsync("libs-one", AdminApiHarness.Token);
        await _api.App.CreateRepositoryAsync("libs-two", AdminApiHarness.Token);

        var preview = await _api.PostAsync("/api/v1/permission-targets/preview", new { repositoryPatterns = new[] { "libs-*" }, includePatterns = new[] { "Acme.*" }, excludePatterns = new[] { "Acme.Internal.*" }, testPackage = "Acme.Internal.Tools" });
        Assert.Equal(new[] { "libs-one", "libs-two" }, preview.Json["repositories"]!.AsArray().Select(r => r!.GetValue<string>()).ToArray());
        Assert.False(preview.Json["test"]!["matches"]!.GetValue<bool>());

        await _api.PostAsync("/api/v1/permission-targets", new { name = "libs", repositoryPatterns = new[] { "libs-one" } });
        var updated = await _api.PutAsync("/api/v1/permission-targets/libs", new { repositoryPatterns = new[] { "libs-*" }, includePatterns = new[] { "Acme.*" } }, password: Password);
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("libs-*", Assert.Single(updated.Json["repositoryPatterns"]!.AsArray())!.GetValue<string>());

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/permission-targets", new { name = "empty" })).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/permission-targets/libs", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/permission-targets/libs")).Status);

        await _api.DeleteAsync("/api/v1/permission-targets/Anyone%20reads%20every%20repository", password: Password);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PostAsync("/api/v1/permission-targets/restore-built-ins", password: Password)).Status);
        Assert.Contains((await _api.GetAsync("/api/v1/permission-targets")).Json["items"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "Anyone reads every repository");
    }

    [Fact]
    public async Task ProjectsHoldRepositoriesRolesAndTargetsAndAreDeletedWithoutTheirRepositories()
    {
        await _api.App.CreateRepositoryAsync("team", AdminApiHarness.Token);
        Assert.Equal(HttpStatusCode.Created, (await _api.PostAsync("/api/v1/projects", new { name = "acme", displayName = "Acme" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/projects", new { name = "" })).Status);

        Assert.Equal("confirmation-required", (await _api.PutAsync("/api/v1/projects/acme/repositories/team")).Code);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PutAsync("/api/v1/projects/acme/repositories/team", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PutAsync("/api/v1/projects/acme/members", new { type = "User", principal = "alice", role = "Admin" }, password: Password)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsync("/api/v1/projects/acme/members", new { type = "Organization", principal = "alice", role = "Admin" }, password: Password)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsync("/api/v1/projects/acme/members", new { type = "User", principal = "alice", role = "Boss" }, password: Password)).Status);

        var project = await _api.GetAsync("/api/v1/projects/acme");
        Assert.Equal("team", Assert.Single(project.Json["repositories"]!.AsArray())!.GetValue<string>());
        Assert.Contains("alice", Assert.Single(project.Json["members"]!.AsArray())!["principal"]!.GetValue<string>());
        Assert.Equal("acme", (await _api.GetAsync("/api/v1/repositories/team")).Json["project"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/projects/acme/members/User/alice", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/projects/acme/repositories/team", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.DeleteAsync("/api/v1/projects/acme/repositories/team", password: Password)).Status);

        await _api.PutAsync("/api/v1/projects/acme/repositories/team", password: Password);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/projects/acme", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/projects/acme")).Status);
        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/v1/repositories/team")).Status);
    }

    [Fact]
    public async Task EveryChangeIsAuditedWithTheKeyOwnersNameAndTheAuditIsFilterable()
    {
        await _api.PostAsync("/api/v1/groups", new { name = "audited" });
        await _api.PostAsync("/api/v1/groups", new { name = "audited-too" }, AdminApiHarness.GlobalKey);

        var byRoot = await _api.GetAsync("/api/v1/audit?actor=root");
        Assert.Contains(byRoot.Json["items"]!.AsArray(), e => e!["subject"]!.GetValue<string>() == "audited");
        Assert.DoesNotContain(byRoot.Json["items"]!.AsArray(), e => e!["subject"]!.GetValue<string>() == "audited-too");

        var byGlobal = await _api.GetAsync("/api/v1/audit?actor=" + Uri.EscapeDataString("(global key)"));
        Assert.Contains(byGlobal.Json["items"]!.AsArray(), e => e!["subject"]!.GetValue<string>() == "audited-too");

        var bySubject = await _api.GetAsync("/api/v1/audit?subject=audited-too");
        Assert.Equal("group.create", Assert.Single(bySubject.Json["items"]!.AsArray())!["action"]!.GetValue<string>());
    }

    [Fact]
    public async Task UsersAreListedShownCreatedPromotedAndTheirKeysRevoked()
    {
        var list = await _api.GetAsync("/api/v1/users?q=ali");
        Assert.Equal("alice", Assert.Single(list.Json["items"]!.AsArray())!["userName"]!.GetValue<string>());

        var key = await _api.KeyAsync("alice", ApiKeyScopes.All);
        var shown = await _api.GetAsync("/api/v1/users/alice");
        var keys = shown.Json["keys"]!.AsArray();
        Assert.Single(keys);
        Assert.DoesNotContain(key, shown.Body);
        Assert.DoesNotContain("hashedKey", shown.Body, StringComparison.OrdinalIgnoreCase);

        var keyNumber = keys[0]!["key"]!.GetValue<int>();
        Assert.Equal("confirmation-required", (await _api.DeleteAsync($"/api/v1/users/alice/keys/{keyNumber}")).Code);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync($"/api/v1/users/alice/keys/{keyNumber}", password: Password)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.DeleteAsync($"/api/v1/users/alice/keys/{keyNumber}", password: Password)).Status);
        Assert.Empty((await _api.GetAsync("/api/v1/users/alice")).Json["keys"]!.AsArray());

        var generated = await _api.PostAsync("/api/v1/users", new { userName = "carol", email = "carol@example.test" }, password: Password);
        Assert.Equal(HttpStatusCode.Created, generated.Status);
        var password = generated.Json["generatedPassword"]!.GetValue<string>();
        Assert.True(await CanSignInAsync(_api.App, "carol", password));

        var given = await _api.PostAsync("/api/v1/users", new { userName = "dave", password = "Another1Pass!" }, password: Password);
        Assert.Equal(HttpStatusCode.Created, given.Status);
        Assert.Null(given.Json["generatedPassword"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/users", new { userName = "dave" }, password: Password)).Status);

        var promoted = await _api.PutAsync("/api/v1/users/carol/administrator", new { isAdministrator = true }, password: Password);
        Assert.True(promoted.Json["isAdministrator"]!.GetValue<bool>());
        var demoted = await _api.PutAsync("/api/v1/users/carol/administrator", new { isAdministrator = false }, password: Password);
        Assert.False(demoted.Json["isAdministrator"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsync("/api/v1/users/carol/administrator", new { }, password: Password)).Status);

        // The only administrator stays one.
        var last = await _api.PutAsync("/api/v1/users/root/administrator", new { isAdministrator = false }, password: Password);
        Assert.Equal(HttpStatusCode.Conflict, last.Status);

        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/users/nobody")).Status);
    }

    [Fact]
    public async Task AUserJoinsAndLeavesAGroupFromTheUserSide()
    {
        await _api.PostAsync("/api/v1/groups", new { name = "ops" });

        Assert.Equal(HttpStatusCode.NoContent, (await _api.PutAsync("/api/v1/users/bob/groups/ops", password: Password)).Status);
        Assert.Contains("ops", (await _api.GetAsync("/api/v1/users/bob")).Json["groups"]!.AsArray().Select(g => g!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/users/bob/groups/ops", password: Password)).Status);
        Assert.Empty((await _api.GetAsync("/api/v1/users/bob")).Json["groups"]!.AsArray());
    }
}

internal static class AdminApiHarnessExtensions
{
    /// <summary>Turns a repository's anonymous read off through the API.</summary>
    public static async Task PatchAnonymousReadOffAsync(this AdminApiHarness api, string repository)
    {
        var answer = await api.SendAsync(HttpMethod.Patch, "/api/v1/repositories/" + repository, new { anonymousRead = false });
        Assert.Equal(HttpStatusCode.OK, answer.Status);
    }
}
