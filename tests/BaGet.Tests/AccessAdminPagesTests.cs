using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// The access-control admin pages (U3): groups, permission targets with the pattern preview and test box, projects
/// with roles, the user page's groups and effective permissions, the repository page's effective access, the audit log
/// and the account's own access. Each change asks for the password where the plan says so, is audited, takes effect on
/// the next request, and is refused with 503 while the maintenance gate is closed.
/// </summary>
public class AccessAdminPagesTests : IDisposable
{
    private const string Second = "second";

    private readonly BaGetApplication _app;
    private readonly Browser _admin;
    private readonly CancellationToken _token = CancellationToken.None;

    public AccessAdminPagesTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        });
        _app.CreateRepositoryAsync(Second).GetAwaiter().GetResult();
        _admin = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        CreateUserAsync(_app, "alice").GetAwaiter().GetResult();
        CreateUserAsync(_app, "bob").GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _app.Dispose();
    }

    private Task<HttpResponseMessage> PostAsync(Browser browser, string page, string? handler, Dictionary<string, string> fields, bool password = true, string? tokenPage = null)
    {
        if (password) fields["password"] = Password;

        return browser.PostAsync(page, fields, action: handler == null ? page : $"{page}?handler={handler}", tokenPage: tokenPage);
    }

    private async Task<string> FollowAsync(Browser browser, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString().TrimStart('/');

        return await browser.GetPageAsync(location);
    }

    private async Task<T> WithAsync<T>(Func<IAccessAdministration, IAccessEvaluator, Task<T>> action)
    {
        using var scope = _app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAccessAdministration>(), scope.ServiceProvider.GetRequiredService<IAccessEvaluator>());
    }

    private async Task AdminAsync(Func<IAccessAdministration, Task> action)
    {
        using var scope = _app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<IAccessAdministration>());
    }

    private async Task<string> IdOfAsync(string user) => (await FindAsync(_app, user))!.Id;

    private async Task<Browser> SignedInAsync(string user)
    {
        var browser = new Browser(_app);
        await SignInAsync(browser, user);

        return browser;
    }

    private async Task<IReadOnlyList<AccessAuditEntry>> AuditAsync() => await WithAsync((a, _) => a.AuditAsync(500, _token));

    [Fact]
    public async Task DetailButtonsKeepTheirTargetWhenAnotherHandlerRenderedTheScreen()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateGroupAsync("team", null, "test", _token);
            await a.CreateTargetAsync("t1", new[] { "*" }, null, null, null, "test", _token);
            await a.CreateProjectAsync("shop", null, "test", _token);
            return 0;
        });

        // Screens rendered by a handler other than the GET one (preview, refused change): every button still posts to
        // its own handler of the same address (the ambient {name} is not carried to a link to another action).
        using var preview = await _admin.PostAsync("admin/permissions/t1", new Dictionary<string, string>
        {
            { "repositoryPatterns", "*" }, { "includePatterns", "" }, { "excludePatterns", "" }, { "testPackage", "" },
        }, action: "admin/permissions/t1?handler=Preview");
        var permissions = await preview.Content.ReadAsStringAsync(_token);
        foreach (var handler in new[] { "Save", "SetGrant", "Delete" })
            Assert.Contains($"formaction=\"/admin/permissions/t1?handler={handler}\"", permissions);

        using var projectPreview = await _admin.PostAsync("admin/projects/shop", new Dictionary<string, string>
        {
            { "targetName", "x" }, { "repositoryPatterns", "*" }, { "includePatterns", "" }, { "excludePatterns", "" }, { "testPackage", "" },
        }, action: "admin/projects/shop?handler=Preview");
        var projects = await projectPreview.Content.ReadAsStringAsync(_token);
        foreach (var handler in new[] { "AssignRepository", "SetMember", "CreateTarget", "Delete" })
            Assert.Contains($"formaction=\"/admin/projects/shop?handler={handler}\"", projects);
    }

    [Fact]
    public async Task OnlyAdministratorsSeeTheAccessPagesAndAnonymousVisitorsAreSentToSignIn()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateGroupAsync("team", null, "test", _token);
            await a.CreateTargetAsync("t1", new[] { "*" }, null, null, null, "test", _token);
            await a.CreateProjectAsync("shop", null, "test", _token);
            return 0;
        });
        using var alice = await SignedInAsync("alice");
        using var anonymous = new Browser(_app);

        foreach (var path in new[]
                 {
                     "admin/groups", "admin/groups/team", "admin/permissions", "admin/permissions/t1", "admin/projects", "admin/projects/shop", "admin/audit",
                 })
        {
            using var asUser = await alice.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, asUser.StatusCode);

            using var asAnonymous = await anonymous.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.OK, asAnonymous.StatusCode);
        }

        using var post = await alice.RawPostAsync("admin/groups", new Dictionary<string, string> { { "name", "evil" } });
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.Null(await WithAsync((a, _) => a.GetGroupAsync("evil", _token)));

        foreach (var path in new[] { "admin/groups", "admin/permissions", "admin/projects", "admin/audit" })
        {
            Assert.Contains($"href=\"/{path}\"", await _admin.GetPageAsync("admin/repositories"));
        }
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryTokenIsRefused()
    {
        using var response = await _admin.RawPostAsync("admin/groups", new Dictionary<string, string> { { "name", "forged" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await WithAsync((a, _) => a.GetGroupAsync("forged", _token)));
    }

    [Fact]
    public async Task AGroupIsCreatedRenamedFilledAndDeletedWithPasswordsWhereGrantsAreAtStake()
    {
        var aliceId = await IdOfAsync("alice");

        var created = await FollowAsync(_admin, await _admin.PostAsync("admin/groups", new Dictionary<string, string> { { "name", "team" }, { "description", "Build people" } }));
        Assert.Contains("Group team was created.", created);
        Assert.Contains("Build people", await _admin.GetPageAsync("admin/groups"));

        using (var duplicate = await _admin.PostAsync("admin/groups", new Dictionary<string, string> { { "name", "TEAM" } }))
        {
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            Assert.Contains("already a group", await duplicate.Content.ReadAsStringAsync(_token));
        }

        var found = await _admin.GetPageAsync("admin/groups/team?q=ALI");
        Assert.Contains($"value=\"{aliceId}\"", found);
        Assert.DoesNotContain("bob", found.Substring(found.IndexOf("member-search-results", StringComparison.Ordinal)));

        // Without the password nothing changes.
        using (var wrong = await _admin.PostAsync("admin/groups/team", new Dictionary<string, string> { { "userId", aliceId }, { "password", "wrong" } }, action: "admin/groups/team?handler=AddMember"))
        {
            Assert.Contains("confirmation is incorrect", await FollowAsync(_admin, wrong));
        }

        Assert.Empty((await WithAsync((a, _) => a.GetGroupAsync("team", _token)))!.Members);

        using (var added = await PostAsync(_admin, "admin/groups/team", "AddMember", new Dictionary<string, string> { { "userId", aliceId } }))
        {
            Assert.Contains("The member was added.", await FollowAsync(_admin, added));
        }

        Assert.Contains("alice", (await _admin.GetPageAsync("admin/groups/team")).Substring(0));
        Assert.Equal(new[] { "alice" }, (await WithAsync((a, _) => a.GetGroupAsync("team", _token)))!.Members.Select(m => m.UserName));

        using (var renamed = await _admin.PostAsync("admin/groups/team", new Dictionary<string, string> { { "newName", "builders" } }, action: "admin/groups/team?handler=Rename"))
        {
            Assert.Contains("Group team is now called builders.", await FollowAsync(_admin, renamed));
        }

        using (var removed = await PostAsync(_admin, "admin/groups/builders", "RemoveMember", new Dictionary<string, string> { { "userId", aliceId } }))
        {
            Assert.Contains("The member was removed.", await FollowAsync(_admin, removed));
        }

        using (var deleted = await PostAsync(_admin, "admin/groups/builders", "Delete", new Dictionary<string, string>()))
        {
            Assert.Contains("Group builders was deleted.", await FollowAsync(_admin, deleted));
        }

        Assert.Null(await WithAsync((a, _) => a.GetGroupAsync("builders", _token)));

        var actions = (await AuditAsync()).Select(e => e.Action).ToList();
        foreach (var expected in new[] { "group.create", "group.member.add", "group.rename", "group.member.remove", "group.delete" })
        {
            Assert.Contains(expected, actions);
        }

        Assert.All((await AuditAsync()).Where(e => e.Action!.StartsWith("group.", StringComparison.Ordinal)), e => Assert.Equal("root", e.Actor));
    }

    [Fact]
    public async Task TheBuiltInGroupsAreShownButNeitherRenamedDeletedNorGivenMembers()
    {
        var html = await _admin.GetPageAsync("admin/groups");
        Assert.Contains("data-group=\"Everyone\"", html);
        Assert.Contains("data-group=\"Authenticated\"", html);

        var detail = await _admin.GetPageAsync("admin/groups/Everyone");
        Assert.Contains("group-builtin", detail);
        Assert.DoesNotContain("asp-page-handler", detail);
        Assert.DoesNotContain("handler=Delete", detail);
        Assert.Contains(BuiltInNames.AnyoneReads, detail);

        using var rename = await _admin.PostAsync("admin/groups/Everyone", new Dictionary<string, string> { { "newName", "All" } }, action: "admin/groups/Everyone?handler=Rename", tokenPage: "admin/groups");
        Assert.Contains("built-in group", await FollowAsync(_admin, rename));

        using var delete = await PostAsync(_admin, "admin/groups/Everyone", "Delete", new Dictionary<string, string>(), tokenPage: "admin/groups");
        Assert.Contains("built-in group", await FollowAsync(_admin, delete));
        Assert.NotNull(await WithAsync((a, _) => a.GetGroupAsync("Everyone", _token)));
    }

    [Fact]
    public async Task APermissionTargetIsCreatedPreviewedGrantedAndDeletedAndTheGrantTakesEffectAtOnce()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateGroupAsync("builders", null, "test", _token);
            await a.AddGroupMemberAsync("builders", await IdOfAsync("bob"), "test", _token);
            await a.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, "test", _token);
            return 0;
        });

        var created = await FollowAsync(_admin, await _admin.PostAsync("admin/permissions", new Dictionary<string, string>
        {
            { "name", "contoso" },
            { "repositoryPatterns", $"{Second}\ndefault" },
            { "includePatterns", "Contoso.*" },
            { "excludePatterns", "Contoso.Internal.*" },
        }));
        Assert.Contains("Permission target contoso was created.", created);

        // The saved target is previewed on GET: matching repositories and the test box.
        var page = await _admin.GetPageAsync("admin/permissions/contoso?testPackage=Contoso.Lib");
        Assert.Contains("id=\"preview-repositories\"", page);
        Assert.Contains($"default, {Second}", page);
        Assert.Contains("data-matches=\"true\"", page);

        var excluded = await _admin.GetPageAsync("admin/permissions/contoso?testPackage=Contoso.Internal.Tools");
        Assert.Contains("data-matches=\"false\"", excluded);
        Assert.Contains("excluded by", excluded);

        // An unsaved edit is previewed without saving.
        using (var preview = await _admin.PostAsync("admin/permissions/contoso", new Dictionary<string, string>
               {
                   { "repositoryPatterns", "nothing-*" }, { "includePatterns", "Other.*" }, { "excludePatterns", "" }, { "testPackage", "Contoso.Lib" },
               }, action: "admin/permissions/contoso?handler=Preview"))
        {
            var html = await preview.Content.ReadAsStringAsync(_token);
            Assert.Contains("preview-repositories-none", html);
            Assert.Contains("data-matches=\"false\"", html);
            Assert.Contains("matches no include pattern", html);
        }

        Assert.Equal(new[] { "default", Second }, (await WithAsync((a, _) => a.GetTargetAsync("contoso", _token)))!.Target.RepositoryPatterns.OrderBy(p => p));

        var bob = new BaGetPrincipal(await IdOfAsync("bob"), "bob", false, ApiKeyScopes.All, "*");
        Assert.False(await WithAsync((_, e) => e.IsAllowedAsync(bob, RepositoryInfo.Default, "Contoso.Lib", AccessAction.Deploy, _token)));

        using (var wrong = await _admin.PostAsync("admin/permissions/contoso", new Dictionary<string, string>
               {
                   { "principalType", "Group" }, { "principalName", "builders" }, { "actions", "Deploy" }, { "password", "wrong" },
               }, action: "admin/permissions/contoso?handler=SetGrant"))
        {
            Assert.Contains("confirmation is incorrect", await FollowAsync(_admin, wrong));
        }

        Assert.Empty((await WithAsync((a, _) => a.GetTargetAsync("contoso", _token)))!.Grants);

        using (var grant = await PostAsync(_admin, "admin/permissions/contoso", "SetGrant", new Dictionary<string, string>
               {
                   { "principalType", "Group" }, { "principalName", "builders" }, { "actions", "Deploy" },
               }))
        {
            Assert.Contains("group builders", await FollowAsync(_admin, grant));
        }

        Assert.True(await WithAsync((_, e) => e.IsAllowedAsync(bob, RepositoryInfo.Default, "Contoso.Lib", AccessAction.Deploy, _token)));
        Assert.False(await WithAsync((_, e) => e.IsAllowedAsync(bob, RepositoryInfo.Default, "Other.Lib", AccessAction.Deploy, _token)));
        Assert.False(await WithAsync((_, e) => e.IsAllowedAsync(bob, RepositoryInfo.Default, "Contoso.Internal.Tools", AccessAction.Deploy, _token)));

        using (var unknown = await PostAsync(_admin, "admin/permissions/contoso", "SetGrant", new Dictionary<string, string>
               {
                   { "principalType", "User" }, { "principalName", "nobody" }, { "actions", "Read" },
               }))
        {
            Assert.Contains("There is no user named", await FollowAsync(_admin, unknown));
        }

        // Prefilled edit form: the existing actions are ticked.
        var edit = await _admin.GetPageAsync("admin/permissions/contoso?principalType=Group&principalName=builders");
        Assert.Matches("value=\"Deploy\" checked", edit);

        var builders = await WithAsync((a, _) => a.ResolvePrincipalAsync(PrincipalType.Group, "builders", _token));
        using (var remove = await PostAsync(_admin, "admin/permissions/contoso", "RemoveGrant", new Dictionary<string, string>
               {
                   { "type", "Group" }, { "principalId", builders! },
               }))
        {
            Assert.Contains("The grant was removed.", await FollowAsync(_admin, remove));
        }

        Assert.False(await WithAsync((_, e) => e.IsAllowedAsync(bob, RepositoryInfo.Default, "Contoso.Lib", AccessAction.Deploy, _token)));

        using (var delete = await PostAsync(_admin, "admin/permissions/contoso", "Delete", new Dictionary<string, string>()))
        {
            Assert.Contains("Permission target contoso was deleted.", await FollowAsync(_admin, delete));
        }

        Assert.Null(await WithAsync((a, _) => a.GetTargetAsync("contoso", _token)));
        var actions = (await AuditAsync()).Select(e => e.Action).ToList();
        foreach (var expected in new[] { "target.create", "grant.set", "target.delete" }) Assert.Contains(expected, actions);
    }

    [Fact]
    public async Task EditingPatternsAsksForThePasswordAndRejectsEmptyRepositoryPatterns()
    {
        await WithAsync((a, _) => a.CreateTargetAsync("t1", new[] { "*" }, null, null, null, "test", _token));

        using (var wrong = await _admin.PostAsync("admin/permissions/t1", new Dictionary<string, string> { { "repositoryPatterns", Second }, { "password", "wrong" } }, action: "admin/permissions/t1?handler=Save"))
        {
            Assert.Contains("confirmation is incorrect", await FollowAsync(_admin, wrong));
        }

        Assert.Equal(new[] { "*" }, (await WithAsync((a, _) => a.GetTargetAsync("t1", _token)))!.Target.RepositoryPatterns);

        using (var empty = await PostAsync(_admin, "admin/permissions/t1", "Save", new Dictionary<string, string> { { "repositoryPatterns", " " } }))
        {
            Assert.Contains("needs at least one repository pattern", await FollowAsync(_admin, empty));
        }

        using (var saved = await PostAsync(_admin, "admin/permissions/t1", "Save", new Dictionary<string, string> { { "repositoryPatterns", Second }, { "includePatterns", "A.*" } }))
        {
            Assert.Contains("The patterns were saved.", await FollowAsync(_admin, saved));
        }

        var target = (await WithAsync((a, _) => a.GetTargetAsync("t1", _token)))!.Target;
        Assert.Equal(new[] { Second }, target.RepositoryPatterns);
        Assert.Equal(new[] { "A.*" }, target.IncludePatterns);
    }

    [Fact]
    public async Task TheBuiltInTargetsAreListedAndRestorableAfterDeletion()
    {
        var list = await _admin.GetPageAsync("admin/permissions");
        Assert.Contains($"data-target=\"{BuiltInNames.AnyoneReads}\"", list);

        await AdminAsync(a => a.DeleteTargetAsync(BuiltInNames.AnyoneReads, "test", _token));
        Assert.DoesNotContain($"data-target=\"{BuiltInNames.AnyoneReads}\"", await _admin.GetPageAsync("admin/permissions"));

        using var restore = await PostAsync(_admin, "admin/permissions", "RestoreBuiltIns", new Dictionary<string, string>());
        Assert.Contains("were restored", await FollowAsync(_admin, restore));
        Assert.NotNull(await WithAsync((a, _) => a.GetTargetAsync(BuiltInNames.AnyoneReads, _token)));
    }

    [Fact]
    public async Task AProjectGetsRepositoriesAndRolesAndItsAdministratorManagesOnlyItsOwnMembersAndTargets()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateProjectAsync("other", null, "test", _token);
            await a.CreateTargetAsync("other target", new[] { "*" }, null, null, (await a.GetProjectAsync("other", _token))!.Project.Key, "test", _token);
            return 0;
        });

        var created = await FollowAsync(_admin, await _admin.PostAsync("admin/projects", new Dictionary<string, string> { { "name", "shop" }, { "displayName", "The shop" } }));
        Assert.Contains("Project shop was created.", created);
        Assert.Contains("The shop", created);

        using (var assign = await PostAsync(_admin, "admin/projects/shop", "AssignRepository", new Dictionary<string, string> { { "repository", Second } }))
        {
            Assert.Contains($"Repository {Second} is now in shop.", await FollowAsync(_admin, assign));
        }

        Assert.Equal("shop", (await WithAsync((_, e) => e.DescribeAsync(new RepositoryInfo(2, Second, RepositoryFormat.NuGet, RepositoryKind.Local), _token))).Project);

        using (var admin = await PostAsync(_admin, "admin/projects/shop", "SetMember", new Dictionary<string, string> { { "type", "User" }, { "principalName", "bob" }, { "role", "Admin" } }))
        {
            Assert.Contains("bob is now Admin of shop.", await FollowAsync(_admin, admin));
        }

        using (var developer = await PostAsync(_admin, "admin/projects/shop", "SetMember", new Dictionary<string, string> { { "type", "User" }, { "principalName", "alice" }, { "role", "Viewer" } }))
        {
            await FollowAsync(_admin, developer);
        }

        Assert.Contains("data-member=\"user bob\"", await _admin.GetPageAsync("admin/projects/shop"));

        // The project's administrator: their project only; no repository assignment, no deletion, nothing of "other".
        using var bob = await SignedInAsync("bob");
        var list = await bob.GetPageAsync("admin/projects");
        Assert.Contains("data-project=\"shop\"", list);
        Assert.DoesNotContain("data-project=\"other\"", list);
        Assert.DoesNotContain("project-create-form", list);
        using (var foreign = await bob.GetAsync("admin/projects/other")) Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using (var foreignTarget = await bob.GetAsync("admin/permissions/other%20target")) Assert.Equal(HttpStatusCode.NotFound, foreignTarget.StatusCode);
        using (var all = await bob.GetAsync("admin/permissions")) Assert.Equal(HttpStatusCode.NotFound, all.StatusCode);
        using (var groups = await bob.GetAsync("admin/groups")) Assert.Equal(HttpStatusCode.NotFound, groups.StatusCode);

        var detail = await bob.GetPageAsync("admin/projects/shop");
        Assert.DoesNotContain("assign-repository-form", detail);
        Assert.DoesNotContain("project-section-delete", detail);

        using (var set = await PostAsync(bob, "admin/projects/shop", "SetMember", new Dictionary<string, string> { { "type", "User" }, { "principalName", "alice" }, { "role", "Developer" } }))
        {
            Assert.Contains("alice is now Developer of shop.", await FollowAsync(bob, set));
        }

        using (var steal = await PostAsync(bob, "admin/projects/shop", "AssignRepository", new Dictionary<string, string> { { "repository", "default" } }))
        {
            Assert.Contains("Only server administrators assign", await FollowAsync(bob, steal));
        }

        using (var deleteProject = await PostAsync(bob, "admin/projects/shop", "Delete", new Dictionary<string, string>()))
        {
            Assert.Contains("Only server administrators delete", await FollowAsync(bob, deleteProject));
        }

        using (var other = await PostAsync(bob, "admin/projects/other", "SetMember", new Dictionary<string, string> { { "type", "User" }, { "principalName", "bob" }, { "role", "Admin" } }, tokenPage: "admin/projects/shop"))
        {
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        }

        // Developer: Read, Annotate, Deploy on the project's repository, set by the project administrator.
        var aliceRepo = new RepositoryInfo(2, Second, RepositoryFormat.NuGet, RepositoryKind.Local);
        var alice = new BaGetPrincipal(await IdOfAsync("alice"), "alice", false, ApiKeyScopes.All, "*");
        Assert.True(await WithAsync((_, e) => e.IsAllowedAsync(alice, aliceRepo, null, AccessAction.Deploy, _token)));

        // The project administrator creates a project target, administers its grants, and cannot reach it from outside.
        var target = await FollowAsync(bob, await bob.PostAsync("admin/projects/shop", new Dictionary<string, string>
        {
            { "targetName", "shop deploy" }, { "repositoryPatterns", "*" }, { "includePatterns", "Shop.*" }, { "excludePatterns", "" }, { "testPackage", "" },
        }, action: "admin/projects/shop?handler=CreateTarget"));
        Assert.Contains("Permission target shop deploy was created.", target);
        Assert.Equal("shop", (await WithAsync((a, _) => a.GetTargetAsync("shop deploy", _token)))!.Target.ProjectName);
        using (var grant = await PostAsync(bob, "admin/permissions/shop%20deploy", "SetGrant", new Dictionary<string, string>
               {
                   { "principalType", "User" }, { "principalName", "alice" }, { "actions", "Delete" },
               }))
        {
            Assert.Contains("user alice", await FollowAsync(bob, grant));
        }

        Assert.Contains("id=\"preview-repositories\">second<", await bob.GetPageAsync("admin/permissions/shop%20deploy"));

        using (var removeMember = await PostAsync(bob, "admin/projects/shop", "RemoveMember", new Dictionary<string, string>
               {
                   { "type", "User" }, { "principalId", alice.UserId! },
               }))
        {
            await FollowAsync(bob, removeMember);
        }

        // The preview of a project target only reaches the project's repositories.
        using (var preview = await bob.PostAsync("admin/projects/shop", new Dictionary<string, string>
               {
                   { "targetName", "x" }, { "repositoryPatterns", "*" }, { "includePatterns", "" }, { "excludePatterns", "" }, { "testPackage", "" },
               }, action: "admin/projects/shop?handler=Preview"))
        {
            Assert.Contains(">second<", await preview.Content.ReadAsStringAsync(_token));
        }

        // Only a server administrator deletes the project; its members, targets and repository link go with it.
        using (var delete = await PostAsync(_admin, "admin/projects/shop", "Delete", new Dictionary<string, string>()))
        {
            Assert.Contains("Project shop was deleted.", await FollowAsync(_admin, delete));
        }

        Assert.Null(await WithAsync((a, _) => a.GetProjectAsync("shop", _token)));
        Assert.Null(await WithAsync((a, _) => a.GetTargetAsync("shop deploy", _token)));
        var actions = (await AuditAsync()).Select(e => e.Action).ToList();
        foreach (var expected in new[] { "project.create", "repository.project", "project.member.set", "project.member.remove", "target.create", "project.delete" })
        {
            Assert.Contains(expected, actions);
        }
    }

    [Fact]
    public async Task AViewerOrDeveloperCannotOpenTheProjectPagesAndAProjectAdministratorSeesTheirLinkOnMyAccess()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateProjectAsync("shop", null, "test", _token);
            await a.SetProjectMemberAsync("shop", PrincipalType.User, await IdOfAsync("alice"), ProjectRole.Developer, "test", _token);
            await a.SetProjectMemberAsync("shop", PrincipalType.User, await IdOfAsync("bob"), ProjectRole.Admin, "test", _token);
            return 0;
        });

        using var alice = await SignedInAsync("alice");
        using (var response = await alice.GetAsync("admin/projects/shop")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await alice.GetAsync("admin/projects")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var bob = await SignedInAsync("bob");
        var mine = await bob.GetPageAsync("account/access");
        Assert.Contains("access-project", mine);
        Assert.Contains("href=\"/admin/projects/shop\"", mine);
        Assert.DoesNotContain("access-project", await alice.GetPageAsync("account/access"));
    }

    [Fact]
    public async Task TheUserPageManagesGroupsAndExplainsTheEffectivePermissionsPerRepository()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateGroupAsync("builders", null, "test", _token);
            await a.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, "test", _token);
            await a.CreateTargetAsync("contoso", new[] { Second }, new[] { "Contoso.*" }, null, null, "test", _token);
            await a.SetGrantAsync("contoso", PrincipalType.Group, (await a.ResolvePrincipalAsync(PrincipalType.Group, "builders", _token))!, AccessAction.Deploy, "test", _token);
            return 0;
        });

        var before = await _admin.GetPageAsync("admin/users/alice");
        Assert.Contains("user-section-access", before);
        Assert.Contains("data-repository=\"second\"", before);
        Assert.DoesNotContain("group builders", before);

        using (var wrong = await _admin.PostAsync("admin/users/alice", new Dictionary<string, string> { { "group", "builders" }, { "password", "wrong" } }, action: "admin/users/alice?handler=AddGroup"))
        {
            Assert.Contains("confirmation is incorrect", await FollowAsync(_admin, wrong));
        }

        using (var added = await PostAsync(_admin, "admin/users/alice", "AddGroup", new Dictionary<string, string> { { "group", "builders" } }))
        {
            Assert.Contains("alice was added to builders.", await FollowAsync(_admin, added));
        }

        var after = await _admin.GetPageAsync("admin/users/alice");
        Assert.Contains("user-groups", after);
        Assert.Contains("Permission target &#x27;contoso&#x27;", after);
        Assert.Contains("group builders", after);

        // The package box applies include patterns.
        var contoso = await _admin.GetPageAsync("admin/users/alice?package=Contoso.Lib");
        var other = await _admin.GetPageAsync("admin/users/alice?package=Other.Lib");
        Assert.Contains("Permission target &#x27;contoso&#x27;", contoso);
        Assert.DoesNotContain("Permission target &#x27;contoso&#x27;", other);

        using (var unknown = await PostAsync(_admin, "admin/users/alice", "AddGroup", new Dictionary<string, string> { { "group", "ghost" } }))
        {
            Assert.Contains("There is no group named", await FollowAsync(_admin, unknown));
        }

        using (var removed = await PostAsync(_admin, "admin/users/alice", "RemoveGroup", new Dictionary<string, string> { { "group", "builders" } }))
        {
            Assert.Contains("alice was removed from builders.", await FollowAsync(_admin, removed));
        }

        Assert.Empty(await WithAsync((a, _) => a.GroupsOfUserAsync(IdOfAsync("alice").GetAwaiter().GetResult(), _token)));

        var administrator = await _admin.GetPageAsync("admin/users/root");
        Assert.Contains("Server administrators hold every action everywhere.", administrator);
    }

    [Fact]
    public async Task TheRepositoryPageShowsWhoEffectivelyHoldsEachActionAndAnonymousReadBeingOff()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateProjectAsync("shop", null, "test", _token);
            await a.SetRepositoryProjectAsync(Second, "shop", "test", _token);
            await a.SetProjectMemberAsync("shop", PrincipalType.User, await IdOfAsync("alice"), ProjectRole.Developer, "test", _token);
            return 0;
        });

        var html = await _admin.GetPageAsync($"admin/repositories/{Second}");
        Assert.Contains("repository-section-access", html);
        Assert.Contains("data-action=\"Read\"", html);
        Assert.Contains("group Everyone", html);
        Assert.Contains("user alice", html);
        Assert.Contains("href=\"/admin/projects/shop\"", html);

        using var off = await _admin.PostAsync($"admin/repositories/{Second}", new Dictionary<string, string> { { "anonymousRead", "false" } });
        var after = await _admin.GetPageAsync($"admin/repositories/{Second}");
        Assert.Contains("Everyone&#x27;s Read does not apply here", after);

        var readRow = after.Substring(after.IndexOf("data-action=\"Read\"", StringComparison.Ordinal));
        readRow = readRow.Substring(0, readRow.IndexOf("</tr>", StringComparison.Ordinal));
        Assert.DoesNotContain("group Everyone", readRow);
    }

    [Fact]
    public async Task TheAuditPageListsChangesAndFiltersThemByActorSubjectAndDate()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateGroupAsync("alpha", null, "carol", _token);
            await a.CreateGroupAsync("beta", null, "dave", _token);
            return 0;
        });

        var all = await _admin.GetPageAsync("admin/audit");
        Assert.Contains("alpha", all);
        Assert.Contains("beta", all);
        Assert.Contains("<code>group.create</code>", all);

        var byActor = await _admin.GetPageAsync("admin/audit?actor=carol");
        Assert.Contains("alpha", byActor);
        Assert.DoesNotContain(">beta<", byActor);

        var bySubject = await _admin.GetPageAsync("admin/audit?subject=bet");
        Assert.DoesNotContain(">alpha<", bySubject);
        Assert.Contains(">beta<", bySubject);

        Assert.Contains("audit-empty", await _admin.GetPageAsync("admin/audit?from=2021-01-01"));
        Assert.DoesNotContain("audit-empty", await _admin.GetPageAsync("admin/audit?from=2020-01-01&to=2020-01-01"));
        Assert.Contains("audit-filter-error", await _admin.GetPageAsync("admin/audit?from=yesterday"));
    }

    [Fact]
    public async Task MyAccessListsOnlyWhatTheSignedInUserHoldsAndRevealsNothingAboutPrivateRepositories()
    {
        await WithAsync(async (a, _) =>
        {
            await a.DeleteTargetAsync(BuiltInNames.AnyoneReads, "test", _token);
            await a.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, "test", _token);
            await a.CreateTargetAsync("alice reads", new[] { "default" }, new[] { "Contoso.*" }, null, null, "test", _token);
            await a.SetGrantAsync("alice reads", PrincipalType.User, await IdOfAsync("alice"), AccessAction.Read, "test", _token);
            return 0;
        });

        using var alice = await SignedInAsync("alice");
        var html = await alice.GetPageAsync("account/access");
        Assert.Contains("data-repository=\"default\"", html);
        Assert.DoesNotContain($"data-repository=\"{Second}\"", html);
        Assert.Contains("alice reads", html.Replace("&#x27;", "'"));
        Assert.Contains("Contoso.*", html);

        using var bob = await SignedInAsync("bob");
        var none = await bob.GetPageAsync("account/access");
        Assert.Contains("access-empty", none);
        Assert.DoesNotContain("data-repository=", none);

        using var anonymous = new Browser(_app);
        using var response = await anonymous.GetAsync("account/access");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains("href=\"/account/access\"", await alice.GetPageAsync("account/access"));
    }

    [Fact]
    public async Task EveryAccessChangeIsRefusedWith503WhileTheMaintenanceGateIsClosedAndReadingStillWorks()
    {
        await WithAsync(async (a, _) =>
        {
            await a.CreateGroupAsync("team", null, "test", _token);
            await a.CreateTargetAsync("t1", new[] { "*" }, null, null, null, "test", _token);
            await a.CreateProjectAsync("shop", null, "test", _token);
            return 0;
        });
        var aliceId = await IdOfAsync("alice");
        var tokenPage = await _admin.TokenAsync("admin/groups");
        _app.Services.GetRequiredService<MaintenanceGate>().Close("Copying the database");

        var posts = new (string Page, string? Handler, Dictionary<string, string> Fields)[]
        {
            ("admin/groups", null, new() { { "name", "closed" } }),
            ("admin/groups/team", "AddMember", new() { { "userId", aliceId } }),
            ("admin/groups/team", "Delete", new()),
            ("admin/permissions", null, new() { { "name", "closed" }, { "repositoryPatterns", "*" } }),
            ("admin/permissions/t1", "SetGrant", new() { { "principalType", "User" }, { "principalName", "alice" }, { "actions", "Read" } }),
            ("admin/projects", null, new() { { "name", "closed" } }),
            ("admin/projects/shop", "SetMember", new() { { "type", "User" }, { "principalName", "alice" }, { "role", "Viewer" } }),
            ("admin/users/alice", "AddGroup", new() { { "group", "team" } }),
        };

        foreach (var (page, handler, fields) in posts)
        {
            fields["password"] = Password;
            fields["__RequestVerificationToken"] = tokenPage;
            using var response = await _admin.RawPostAsync(handler == null ? page : $"{page}?handler={handler}", fields);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        Assert.Null(await WithAsync((a, _) => a.GetGroupAsync("closed", _token)));
        Assert.Empty((await WithAsync((a, _) => a.GetGroupAsync("team", _token)))!.Members);
        Assert.Contains("data-group=\"team\"", await _admin.GetPageAsync("admin/groups"));
        await _admin.GetPageAsync("admin/audit");
    }
}
