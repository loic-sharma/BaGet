using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// What the access admin pages are built on: the evaluator's explanation (<see cref="IAccessEvaluator.ExplainAsync"/>)
/// and repository summary (<see cref="IAccessEvaluator.DescribeAsync"/>), the read side of
/// <see cref="IAccessAdministration"/> (groups, targets, projects, user roles, filtered audit) and the pattern helpers
/// the previews use.
/// </summary>
public class AccessInspectionTests : IDisposable
{
    private const string Second = "second";

    private readonly BaGetApplication _app;
    private readonly CancellationToken _token = CancellationToken.None;
    private readonly RepositoryInfo _second;

    public AccessInspectionTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
            { "Accounts:Enabled", "true" },
        });
        _app.CreateRepositoryAsync(Second).GetAwaiter().GetResult();
        using var scope = _app.Services.CreateScope();
        _second = scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(Second, _token).GetAwaiter().GetResult()!;
        CreateUserAsync(_app, "alice").GetAwaiter().GetResult();
        CreateUserAsync(_app, "bob").GetAwaiter().GetResult();
    }

    public void Dispose() => _app.Dispose();

    private async Task<T> WithAsync<T>(Func<IAccessEvaluator, IAccessAdministration, Task<T>> action)
    {
        using var scope = _app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAccessEvaluator>(), scope.ServiceProvider.GetRequiredService<IAccessAdministration>());
    }

    private async Task AdminAsync(Func<IAccessAdministration, Task> action)
    {
        using var scope = _app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<IAccessAdministration>());
    }

    private async Task<BaGetPrincipal> PrincipalAsync(string name)
    {
        var user = await FindAsync(_app, name);
        return new BaGetPrincipal(user!.Id, user.UserName, false, ApiKeyScopes.All, "*", user.IsAdministrator);
    }

    [Theory]
    [InlineData("Contoso.*", "contoso.lib", true)]
    [InlineData("Contoso.*", "Other.Lib", false)]
    [InlineData("*", "Anything", true)]
    [InlineData("a*c", "ABBBC", true)]
    [InlineData("Exact", "exact", true)]
    [InlineData("Exact", "Exact.More", false)]
    public void PatternsMatchLikeTheEvaluator(string pattern, string value, bool expected)
        => Assert.Equal(expected, AccessPatterns.IsMatch(new[] { pattern }, value));

    [Fact]
    public void TheTestBoxFollowsTheEvaluatorsRule()
    {
        Assert.True(AccessPatterns.Test(new[] { "Contoso.*" }, new[] { "Contoso.Internal.*" }, "Contoso.Lib").Matches);
        var excluded = AccessPatterns.Test(new[] { "Contoso.*" }, new[] { "Contoso.Internal.*" }, "Contoso.Internal.Tools");
        Assert.False(excluded.Matches);
        Assert.Contains("excluded by 'Contoso.Internal.*'", excluded.Explanation);
        var outside = AccessPatterns.Test(new[] { "Contoso.*" }, null, "Other.Lib");
        Assert.False(outside.Matches);
        Assert.Contains("no include pattern", outside.Explanation);
        Assert.True(AccessPatterns.Test(null, null, "Any.Thing").Matches);

        Assert.Equal(new[] { "a", "b", "c" }, AccessPatterns.Split(" a,\r\nb ;c\n\n"));
        Assert.Null(AccessPatterns.DescribeScope(new[] { "*" }, null));
        Assert.Equal("Contoso.* except X.*", AccessPatterns.DescribeScope(new[] { "Contoso.*" }, new[] { "X.*" }));
    }

    [Fact]
    public async Task ExplainNamesTheSeededTargetsThatGrantAndTheOverridesThatBypass()
    {
        var alice = await PrincipalAsync("alice");

        var explanation = await WithAsync((e, _) => e.ExplainAsync(alice, _second, "Any.Lib", _token));
        Assert.Equal(AccessAction.All, explanation.Actions);
        Assert.Null(explanation.Override);
        Assert.Contains(explanation.Reasons, r => r.Source.Contains(BuiltInNames.AnyoneReads) && r.Principal == "group Everyone" && r.Effective == AccessAction.Read);
        Assert.Contains(explanation.Reasons, r => r.Source.Contains(BuiltInNames.AuthenticatedDeploy) && r.Principal == "group Authenticated");

        var anonymous = await WithAsync((e, _) => e.ExplainAsync(null, _second, null, _token));
        Assert.Equal(AccessAction.Read, anonymous.Actions);

        var admin = await WithAsync((e, _) => e.ExplainAsync(new BaGetPrincipal("x", "root", false, ApiKeyScopes.All, "*", isAdministrator: true), _second, null, _token));
        Assert.Equal(AccessAction.All, admin.Actions);
        Assert.Contains("administrators", admin.Override);

        var superuser = await WithAsync((e, _) => e.ExplainAsync(BaGetPrincipal.Superuser, _second, null, _token));
        Assert.Contains("global API key", superuser.Override);
    }

    [Fact]
    public async Task ExplainAgreesWithIsAllowedAndReportsAnonymousReadBeingOff()
    {
        await WithAsync(async (_, a) =>
        {
            await a.CreateGroupAsync("builders", null, "test", _token);
            await a.CreateTargetAsync("contoso only", new[] { Second }, new[] { "Contoso.*" }, new[] { "Contoso.Internal.*" }, null, "test", _token);
            await a.SetGrantAsync("contoso only", PrincipalType.Group, (await a.ResolvePrincipalAsync(PrincipalType.Group, "builders", _token))!, AccessAction.Deploy, "test", _token);
            await a.AddGroupMemberAsync("builders", (await FindAsync(_app, "bob"))!.Id, "test", _token);
            return 0;
        });
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync(Second, false, _token, "test");
        }

        var bob = await PrincipalAsync("bob");

        foreach (var id in new[] { "Contoso.Lib", "Contoso.Internal.Tools", "Other.Lib", null })
        {
            foreach (var action in new[] { AccessAction.Read, AccessAction.Deploy })
            {
                var allowed = await WithAsync((e, _) => e.IsAllowedAsync(bob, _second, id, action, _token));
                var explained = await WithAsync((e, _) => e.ExplainAsync(bob, _second, id, _token));
                Assert.Equal(allowed, explained.Has(action));
            }
        }

        var deploy = await WithAsync((e, _) => e.ExplainAsync(bob, _second, "Contoso.Lib", _token));
        Assert.Contains(deploy.Reasons, r => r.Source.Contains("contoso only") && r.Principal == "group builders" && r.Packages == "Contoso.* except Contoso.Internal.*");

        var anonymous = await WithAsync((e, _) => e.ExplainAsync(null, _second, null, _token));
        Assert.Equal(AccessAction.None, anonymous.Actions);
        Assert.Contains(anonymous.Reasons, r => r.Note != null && r.Note.Contains("Anonymous read is off") && r.Effective == AccessAction.None);
    }

    [Fact]
    public async Task DescribeListsWhoHoldsWhatInARepositoryIncludingProjectRoles()
    {
        await WithAsync(async (_, a) =>
        {
            await a.CreateProjectAsync("shop", "Shop", "test", _token);
            await a.SetRepositoryProjectAsync(Second, "shop", "test", _token);
            await a.SetProjectMemberAsync("shop", PrincipalType.User, (await FindAsync(_app, "alice"))!.Id, ProjectRole.Developer, "test", _token);
            return 0;
        });

        var summary = await WithAsync((e, _) => e.DescribeAsync(_second, _token));

        Assert.True(summary.ModelAvailable);
        Assert.True(summary.AnonymousRead);
        Assert.Equal("shop", summary.Project);
        Assert.Contains("group Everyone", summary.Who(AccessAction.Read));
        Assert.Contains("user alice", summary.Who(AccessAction.Deploy));
        Assert.Contains("group Authenticated", summary.Who(AccessAction.Manage));
        Assert.DoesNotContain("user alice", summary.Who(AccessAction.Manage));
    }

    [Fact]
    public async Task GroupsTargetsAndProjectsAreReadBackWithNamesAndCounts()
    {
        var alice = (await FindAsync(_app, "alice"))!;
        await WithAsync(async (_, a) =>
        {
            await a.CreateGroupAsync("builders", "Build people", "test", _token);
            await a.AddGroupMemberAsync("builders", alice.Id, "test", _token);
            await a.CreateTargetAsync("t1", new[] { "*" }, null, null, null, "test", _token);
            await a.SetGrantAsync("t1", PrincipalType.Group, (await a.ResolvePrincipalAsync(PrincipalType.Group, "BUILDERS", _token))!, AccessAction.Deploy | AccessAction.Read, "test", _token);
            await a.SetGrantAsync("t1", PrincipalType.User, alice.Id, AccessAction.Manage, "test", _token);
            await a.CreateProjectAsync("shop", null, "test", _token);
            await a.SetProjectMemberAsync("shop", PrincipalType.Group, (await a.ResolvePrincipalAsync(PrincipalType.Group, "builders", _token))!, ProjectRole.Admin, "test", _token);
            return 0;
        });

        var groups = await WithAsync((_, a) => a.ListGroupsAsync(_token));
        Assert.Equal(new[] { BuiltInNames.Authenticated, BuiltInNames.Everyone }, groups.Where(g => g.IsBuiltIn).Select(g => g.Name).OrderBy(n => n));
        Assert.Equal(1, groups.Single(g => g.Name == "builders").MemberCount);
        Assert.True(groups[0].IsBuiltIn);

        var group = await WithAsync((_, a) => a.GetGroupAsync("builders", _token));
        Assert.Equal("alice", group!.Members.Single().UserName);
        var grant = group.Grants.Single();
        Assert.Equal("t1", grant.Target);
        Assert.Equal(AccessAction.Deploy | AccessAction.Read, grant.Actions);
        Assert.Null(await WithAsync((_, a) => a.GetGroupAsync("nope", _token)));

        var target = await WithAsync((_, a) => a.GetTargetAsync("T1", _token));
        Assert.Equal(2, target!.Target.GrantCount);
        Assert.Contains(target.Grants, g => g.Principal == "user alice" && g.Actions == AccessAction.Manage);
        Assert.Contains(target.Grants, g => g.Principal == "group builders");

        var roles = await WithAsync((_, a) => a.ProjectRolesOfUserAsync(alice.Id, _token));
        var role = Assert.Single(roles);
        Assert.Equal(("shop", ProjectRole.Admin), (role.Project, role.Role));
        Assert.Contains("builders", role.Via);
        Assert.True(await WithAsync((_, a) => a.IsProjectAdminAsync(alice.Id, "shop", _token)));
        Assert.False(await WithAsync((_, a) => a.IsProjectAdminAsync((FindAsync(_app, "bob").GetAwaiter().GetResult())!.Id, "shop", _token)));
        Assert.Equal(new[] { "builders" }, await WithAsync((_, a) => a.GroupsOfUserAsync(alice.Id, _token)));

        var project = await WithAsync((_, a) => a.GetProjectAsync("shop", _token));
        Assert.Equal(1, project!.Project.MemberCount);
        Assert.Equal("group builders", project.Members.Single().Principal);
    }

    [Fact]
    public async Task RenamingAGroupKeepsItsMembershipAndGrantsAndBuiltInsCannotBeRenamed()
    {
        var alice = (await FindAsync(_app, "alice"))!;
        await WithAsync(async (_, a) =>
        {
            await a.CreateGroupAsync("builders", null, "test", _token);
            await a.AddGroupMemberAsync("builders", alice.Id, "test", _token);
            await a.CreateGroupAsync("other", null, "test", _token);
            await a.RenameGroupAsync("builders", "Builders Team", "test", _token);
            return 0;
        });

        Assert.Null(await WithAsync((_, a) => a.GetGroupAsync("builders", _token)));
        Assert.Equal("alice", (await WithAsync((_, a) => a.GetGroupAsync("builders team", _token)))!.Members.Single().UserName);

        await Assert.ThrowsAsync<AccessModelException>(() => AdminAsync(a => a.RenameGroupAsync("Builders Team", "other", "test", _token)));
        await Assert.ThrowsAsync<AccessModelException>(() => AdminAsync(a => a.RenameGroupAsync(BuiltInNames.Everyone, "All", "test", _token)));
        await Assert.ThrowsAsync<AccessModelException>(() => AdminAsync(a => a.RenameGroupAsync("other", "bad/name", "test", _token)));
    }

    [Fact]
    public async Task DeletingAProjectRemovesItsMembersAndTargetsAndFreesItsRepositories()
    {
        await WithAsync(async (_, a) =>
        {
            var project = await a.CreateProjectAsync("shop", null, "test", _token);
            await a.SetRepositoryProjectAsync(Second, "shop", "test", _token);
            await a.CreateTargetAsync("shop deploy", new[] { "*" }, null, null, project.Key, "test", _token);
            await a.SetProjectMemberAsync("shop", PrincipalType.User, (await FindAsync(_app, "alice"))!.Id, ProjectRole.Admin, "test", _token);
            await a.DeleteProjectAsync("shop", "test", _token);
            return 0;
        });

        Assert.Null(await WithAsync((_, a) => a.GetProjectAsync("shop", _token)));
        Assert.Null(await WithAsync((_, a) => a.GetTargetAsync("shop deploy", _token)));
        Assert.Null((await WithAsync((e, _) => e.DescribeAsync(_second, _token))).Project);
        await Assert.ThrowsAsync<AccessModelException>(() => AdminAsync(a => a.DeleteProjectAsync("shop", "test", _token)));
    }

    [Fact]
    public async Task TheAuditLogIsFilteredByActorSubjectAndDate()
    {
        await WithAsync(async (_, a) =>
        {
            await a.CreateGroupAsync("alpha", null, "carol", _token);
            await a.CreateGroupAsync("beta", null, "dave", _token);
            return 0;
        });

        Assert.Equal(new[] { "alpha" }, (await WithAsync((_, a) => a.AuditAsync(new AuditFilter(Actor: "CAROL"), 10, _token))).Select(e => e.Subject));
        Assert.Equal(new[] { "beta" }, (await WithAsync((_, a) => a.AuditAsync(new AuditFilter(Subject: "BET"), 10, _token))).Select(e => e.Subject));
        Assert.Empty(await WithAsync((_, a) => a.AuditAsync(new AuditFilter(Actor: "carol", Subject: "beta"), 10, _token)));
        Assert.Empty(await WithAsync((_, a) => a.AuditAsync(new AuditFilter(From: new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)), 10, _token)));
        Assert.NotEmpty(await WithAsync((_, a) => a.AuditAsync(new AuditFilter(From: new DateTime(2019, 12, 31, 0, 0, 0, DateTimeKind.Utc), To: new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)), 10, _token)));
    }

    [Fact]
    public async Task PrincipalsAreResolvedByNameAndUsersAreSearchedByPartOfTheirName()
    {
        await AdminTestSupport.AddOrganizationAsync(_app, "Contoso", ("alice", true));

        Assert.Equal((await FindAsync(_app, "alice"))!.Id, await WithAsync((_, a) => a.ResolvePrincipalAsync(PrincipalType.User, "ALICE", _token)));
        Assert.NotNull(await WithAsync((_, a) => a.ResolvePrincipalAsync(PrincipalType.Organization, "contoso", _token)));
        Assert.Null(await WithAsync((_, a) => a.ResolvePrincipalAsync(PrincipalType.Group, "missing", _token)));
        Assert.Null(await WithAsync((_, a) => a.ResolvePrincipalAsync(PrincipalType.User, " ", _token)));

        Assert.Equal(new[] { "alice" }, (await WithAsync((_, a) => a.SearchUsersAsync("LIC", 10, _token))).Select(u => u.UserName));
    }
}
