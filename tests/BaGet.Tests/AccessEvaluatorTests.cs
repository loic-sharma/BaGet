using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The access model's decisions (<see cref="IAccessEvaluator"/>) and its administration
/// (<see cref="IAccessAdministration"/>), on the model the migration seeds.
/// </summary>
public class AccessEvaluatorTests : IDisposable
{
    private const string Second = "second";

    private readonly ITestOutputHelper _output;
    private readonly BaGetApplication _app;
    private readonly CancellationToken _token = CancellationToken.None;

    public AccessEvaluatorTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global-key" },
            { "Accounts:Enabled", "true" },
        });
        _app.CreateRepositoryAsync(Second).GetAwaiter().GetResult();
    }

    public void Dispose() => _app.Dispose();

    private static BaGetPrincipal User(string id, bool administrator = false)
        => new(id, id, isSuperuser: false, ApiKeyScopes.All, "*", administrator);

    // Group membership names a user row (UserId is a foreign key); the tests' principals use the name as id.
    private Task AddUsersAsync(params string[] ids) => WithAsync(async (_, _, context) =>
    {
        foreach (var id in ids) context.Users.Add(new BaGetUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        await context.SaveChangesAsync(_token);

        return 0;
    });

    private static readonly RepositoryInfo Default = RepositoryInfo.Default;
    private static readonly RepositoryInfo SecondRepo = new(2, Second, RepositoryFormat.NuGet, RepositoryKind.Local);

    private async Task<T> WithAsync<T>(Func<IAccessEvaluator, IAccessAdministration, IContext, Task<T>> action)
    {
        using var scope = _app.Services.CreateScope();
        var services = scope.ServiceProvider;
        return await action(
            services.GetRequiredService<IAccessEvaluator>(),
            services.GetRequiredService<IAccessAdministration>(),
            services.GetRequiredService<IContext>());
    }

    private Task AdministerAsync(Func<IAccessAdministration, Task> action)
        => WithAsync<int>(async (_, admin, _) =>
        {
            await action(admin);
            return 0;
        });

    private Task<bool> AllowedAsync(BaGetPrincipal? principal, RepositoryInfo repository, string? id, AccessAction action)
        => WithAsync((evaluator, _, _) => evaluator.IsAllowedAsync(principal, repository, id, action, _token));

    private Task<IReadOnlyList<string>> ReadableAsync(BaGetPrincipal? principal)
        => WithAsync(async (evaluator, _, _) =>
            (IReadOnlyList<string>)(await evaluator.ReadableRepositoriesAsync(principal, _token)).Select(r => r.Name).ToList());

    private Task RemoveSeededTargetsAsync()
        => AdministerAsync(async a =>
        {
            await a.DeleteTargetAsync(BuiltInNames.AnyoneReads, "test", _token);
            await a.DeleteTargetAsync(BuiltInNames.AuthenticatedDeploy, "test", _token);
        });

    [Fact]
    public async Task TheSeededModelIsTodaysBehaviour()
    {
        var alice = User("alice");

        foreach (var repository in new[] { Default, SecondRepo })
        {
            Assert.True(await AllowedAsync(null, repository, null, AccessAction.Read));
            Assert.True(await AllowedAsync(null, repository, "Any.Package", AccessAction.Read));
            Assert.False(await AllowedAsync(null, repository, "Any.Package", AccessAction.Deploy));

            Assert.True(await AllowedAsync(alice, repository, "Any.Package", AccessAction.Read));
            foreach (var action in new[] { AccessAction.Deploy, AccessAction.Delete, AccessAction.Annotate, AccessAction.Manage })
            {
                Assert.True(await AllowedAsync(alice, repository, "Any.Package", action));
            }
        }
    }

    [Fact]
    public async Task AdministratorsAndTheSuperuserHoldEveryActionEvenWithNoGrantsAtAll()
    {
        await RemoveSeededTargetsAsync();

        Assert.True(await AllowedAsync(User("root", administrator: true), SecondRepo, "X", AccessAction.All));
        Assert.True(await AllowedAsync(BaGetPrincipal.Superuser, SecondRepo, "X", AccessAction.All));
        Assert.False(await AllowedAsync(User("alice"), SecondRepo, "X", AccessAction.Read));
        Assert.False(await AllowedAsync(null, SecondRepo, "X", AccessAction.Read));
        Assert.Equal(new[] { Repository.DefaultName, Second }, await ReadableAsync(User("root", administrator: true)));
        Assert.Empty(await ReadableAsync(User("alice")));
    }

    [Fact]
    public async Task AGroupGrantGivesDeployOnTheNextRequestAndRemovalTakesItBack()
    {
        await RemoveSeededTargetsAsync();
        await AdministerAsync(async a =>
        {
            await a.CreateGroupAsync("builders", null, "test", _token);
            await a.CreateTargetAsync("second deploy", new[] { Second }, null, null, null, "test", _token);
            await a.SetGrantAsync("second deploy", PrincipalType.Group, await GroupKeyAsync("builders"), AccessAction.Deploy, "test", _token);
        });
        var bob = User("bob");
        await AddUsersAsync("bob");

        Assert.False(await AllowedAsync(bob, SecondRepo, "Any.Lib", AccessAction.Deploy));

        await AdministerAsync(a => a.AddGroupMemberAsync("builders", "bob", "test", _token));
        Assert.True(await AllowedAsync(bob, SecondRepo, "Any.Lib", AccessAction.Deploy));
        Assert.False(await AllowedAsync(bob, Default, "Any.Lib", AccessAction.Deploy));
        Assert.False(await AllowedAsync(bob, SecondRepo, "Any.Lib", AccessAction.Delete));

        await AdministerAsync(a => a.RemoveGroupMemberAsync("builders", "bob", "test", _token));
        Assert.False(await AllowedAsync(bob, SecondRepo, "Any.Lib", AccessAction.Deploy));
    }

    private Task<string> GroupKeyAsync(string name)
        => WithAsync(async (_, _, context) => (await context.Groups!.AsNoTracking().SingleAsync(g => g.Name == name)).Key.ToString());

    [Fact]
    public async Task IncludeAndExcludePatternsNarrowAPackageLevelCheckButNotARepositoryLevelOne()
    {
        await RemoveSeededTargetsAsync();
        await AdministerAsync(async a =>
        {
            await a.CreateTargetAsync("contoso", new[] { "*" }, new[] { "Contoso.*" }, new[] { "Contoso.Secret*" }, null, "test", _token);
            await a.SetGrantAsync("contoso", PrincipalType.User, "carol", AccessAction.Deploy | AccessAction.Read, "test", _token);
        });
        var carol = User("carol");

        Assert.True(await AllowedAsync(carol, Default, "Contoso.Lib", AccessAction.Deploy));
        Assert.True(await AllowedAsync(carol, Default, "contoso.lib", AccessAction.Deploy));
        Assert.False(await AllowedAsync(carol, Default, "Other.Lib", AccessAction.Deploy));
        Assert.False(await AllowedAsync(carol, Default, "Contoso.Secrets", AccessAction.Deploy));
        Assert.False(await AllowedAsync(carol, Default, "Other.Lib", AccessAction.Read));

        // Some package of the repository matches, so the repository is listed and its feed opens.
        Assert.True(await AllowedAsync(carol, Default, null, AccessAction.Read));
    }

    [Fact]
    public async Task ProjectRolesApplyToTheProjectsRepositoriesOnly()
    {
        await RemoveSeededTargetsAsync();
        await AdministerAsync(async a =>
        {
            await a.CreateProjectAsync("team", "Team", "test", _token);
            await a.SetProjectMemberAsync("team", PrincipalType.User, "viewer", ProjectRole.Viewer, "test", _token);
            await a.SetProjectMemberAsync("team", PrincipalType.User, "dev", ProjectRole.Developer, "test", _token);
            await a.SetProjectMemberAsync("team", PrincipalType.User, "boss", ProjectRole.Admin, "test", _token);
        });
        await WithAsync<int>(async (_, _, context) =>
        {
            var project = await context.Projects!.SingleAsync(p => p.Name == "team");
            (await context.Repositories!.SingleAsync(r => r.Name == Second)).ProjectKey = project.Key;
            await context.SaveChangesAsync(_token);
            return 0;
        });

        Assert.True(await AllowedAsync(User("viewer"), SecondRepo, "X", AccessAction.Read));
        Assert.False(await AllowedAsync(User("viewer"), SecondRepo, "X", AccessAction.Deploy));

        Assert.True(await AllowedAsync(User("dev"), SecondRepo, "X", AccessAction.Read | AccessAction.Annotate | AccessAction.Deploy));
        Assert.False(await AllowedAsync(User("dev"), SecondRepo, "X", AccessAction.Delete));
        Assert.False(await AllowedAsync(User("dev"), SecondRepo, "X", AccessAction.Manage));

        Assert.True(await AllowedAsync(User("boss"), SecondRepo, "X", AccessAction.All));

        Assert.False(await AllowedAsync(User("boss"), Default, "X", AccessAction.Read));
        Assert.False(await AllowedAsync(User("dev"), Default, "X", AccessAction.Deploy));
    }

    [Fact]
    public async Task AGroupCanHoldAProjectRoleAndAnOrganizationCanBeGranted()
    {
        await RemoveSeededTargetsAsync();
        await AdminTestSupport.CreateUserAsync(_app, "dana");
        var dana = await AdminTestSupport.FindAsync(_app, "dana");
        await AdminTestSupport.AddOrganizationAsync(_app, "contoso", ("dana", false));
        var organization = await WithAsync(async (_, _, context) => (await context.Organizations!.SingleAsync()).Key.ToString());

        await AdministerAsync(async a =>
        {
            await a.CreateTargetAsync("org", new[] { Second }, null, null, null, "test", _token);
            await a.SetGrantAsync("org", PrincipalType.Organization, organization, AccessAction.Deploy, "test", _token);
        });

        Assert.True(await AllowedAsync(User(dana!.Id), SecondRepo, "X", AccessAction.Deploy));
        Assert.False(await AllowedAsync(User("someone-else"), SecondRepo, "X", AccessAction.Deploy));
    }

    [Fact]
    public async Task TheAnonymousReadFlagGatesTheEveryoneGroupsReadOnThatRepository()
    {
        await AdministerAsync(async _ => { });
        await SetAnonymousReadAsync(Second, false);

        Assert.False(await AllowedAsync(null, SecondRepo, null, AccessAction.Read));
        Assert.False(await AllowedAsync(User("alice"), SecondRepo, null, AccessAction.Read));
        Assert.True(await AllowedAsync(null, Default, null, AccessAction.Read));
        Assert.Equal(new[] { Repository.DefaultName }, await ReadableAsync(null));
        Assert.Equal(new[] { Repository.DefaultName }, await ReadableAsync(User("alice")));

        // A Read grant of their own still opens it.
        await AdministerAsync(async a =>
        {
            await a.CreateTargetAsync("alice reads", new[] { Second }, null, null, null, "test", _token);
            await a.SetGrantAsync("alice reads", PrincipalType.User, "alice", AccessAction.Read, "test", _token);
        });
        Assert.True(await AllowedAsync(User("alice"), SecondRepo, null, AccessAction.Read));
        Assert.False(await AllowedAsync(User("bob"), SecondRepo, null, AccessAction.Read));

        await SetAnonymousReadAsync(Second, true);
        Assert.True(await AllowedAsync(null, SecondRepo, null, AccessAction.Read));
    }

    [Fact]
    public async Task TheAnonymousReadFlagAlsoGatesAProjectRoleHeldByEveryone()
    {
        await RemoveSeededTargetsAsync();
        await AdministerAsync(async a =>
        {
            await a.CreateProjectAsync("team", "Team", "test", _token);
            await a.SetProjectMemberAsync("team", PrincipalType.Group, await GroupKeyAsync("Everyone"), ProjectRole.Developer, "test", _token);
        });
        await WithAsync<int>(async (_, _, context) =>
        {
            var project = await context.Projects!.SingleAsync(p => p.Name == "team");
            (await context.Repositories!.SingleAsync(r => r.Name == Second)).ProjectKey = project.Key;
            await context.SaveChangesAsync(_token);
            return 0;
        });
        await SetAnonymousReadAsync(Second, false);

        Assert.False(await AllowedAsync(null, SecondRepo, null, AccessAction.Read));
        Assert.False(await AllowedAsync(User("alice"), SecondRepo, "X", AccessAction.Read));
        Assert.Empty(await ReadableAsync(null));
        // Only Read is gated; the role's other actions still apply to signed-in callers.
        Assert.True(await AllowedAsync(User("alice"), SecondRepo, "X", AccessAction.Deploy));

        var summary = await WithAsync(async (evaluator, _, _) => await evaluator.DescribeAsync(SecondRepo, _token));
        var role = Assert.Single(summary.Holders, h => h.Source.StartsWith("Project", StringComparison.Ordinal));
        Assert.False(role.Effective.HasFlag(AccessAction.Read));
        Assert.Contains("Anonymous read is off", role.Note);

        await SetAnonymousReadAsync(Second, true);
        Assert.True(await AllowedAsync(null, SecondRepo, null, AccessAction.Read));
    }

    [Fact]
    public async Task TurningAnonymousReadOnAgainRestoresAGrantAnAdministratorDeleted()
    {
        await AdministerAsync(a => a.DeleteTargetAsync(BuiltInNames.AnyoneReads, "test", _token));
        Assert.False(await AllowedAsync(null, SecondRepo, null, AccessAction.Read));

        await SetAnonymousReadAsync(Second, true);

        Assert.True(await AllowedAsync(null, SecondRepo, null, AccessAction.Read));
        Assert.False(await AllowedAsync(null, Default, null, AccessAction.Read));
        var created = await WithAsync(async (_, _, context) => await context.PermissionTargets!.AsNoTracking().Select(t => t.Name).ToListAsync());
        Assert.Contains("Anonymous read second", created);
    }

    private async Task SetAnonymousReadAsync(string name, bool value)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>().SetAnonymousReadAsync(name, value, _token, "test");
    }

    [Fact]
    public async Task EveryChangeIsAudited()
    {
        await AddUsersAsync("u");
        await AdministerAsync(async a =>
        {
            await a.CreateGroupAsync("g", null, "root", _token);
            await a.CreateTargetAsync("t", new[] { "*" }, null, null, null, "root", _token);
            await a.SetGrantAsync("t", PrincipalType.User, "u", AccessAction.Read, "root", _token);
            await a.AddGroupMemberAsync("g", "u", "root", _token);
            await a.DeleteGroupAsync("g", "root", _token);
        });
        await SetAnonymousReadAsync(Second, false);

        var entries = await WithAsync(async (_, admin, _) => await admin.AuditAsync(50, _token));
        Assert.Equal(
            new[] { "repository.anonymous-read", "group.delete", "group.member.add", "grant.set", "target.create", "group.create" },
            entries.Select(e => e.Action).ToArray());
        Assert.All(entries.Take(1), e => Assert.Equal("test", e.Actor));
        Assert.All(entries.Skip(1), e => Assert.Equal("root", e.Actor));
        Assert.Equal(Second, entries[0].Subject);
    }

    [Fact]
    public async Task BuiltInGroupsCannotBeDeletedOrGivenMembersAndNamesAreValidated()
    {
        await Assert.ThrowsAsync<AccessModelException>(() => AdministerAsync(a => a.DeleteGroupAsync("Everyone", null, _token)));
        await Assert.ThrowsAsync<AccessModelException>(() => AdministerAsync(a => a.AddGroupMemberAsync("authenticated", "u", null, _token)));
        await Assert.ThrowsAsync<AccessModelException>(() => AdministerAsync(a => a.CreateGroupAsync("everyone", null, null, _token)));
        await Assert.ThrowsAsync<AccessModelException>(() => AdministerAsync(a => a.CreateGroupAsync("bad/name", null, null, _token)));
        await Assert.ThrowsAsync<AccessModelException>(() => AdministerAsync(a => a.CreateTargetAsync("t", Array.Empty<string>(), null, null, null, null, _token)));
    }

    [Fact]
    public async Task DeletingAGroupRemovesItsMembersGrantsAndProjectRoles()
    {
        await AddUsersAsync("u");
        await AdministerAsync(async a =>
        {
            await a.CreateGroupAsync("g", null, null, _token);
            await a.AddGroupMemberAsync("g", "u", null, _token);
            await a.CreateTargetAsync("t", new[] { "*" }, null, null, null, null, _token);
            await a.SetGrantAsync("t", PrincipalType.Group, await GroupKeyAsync("g"), AccessAction.Read, null, _token);
            await a.CreateProjectAsync("p", null, null, _token);
            await a.SetProjectMemberAsync("p", PrincipalType.Group, await GroupKeyAsync("g"), ProjectRole.Viewer, null, _token);
            await a.DeleteGroupAsync("g", null, _token);
        });

        await WithAsync<int>(async (_, _, context) =>
        {
            Assert.Empty(await context.GroupMembers!.ToListAsync());
            Assert.Empty(await context.ProjectMembers!.ToListAsync());
            Assert.Empty(await context.PermissionGrants!.Where(g => g.TargetKey != 1 && g.TargetKey != 2).ToListAsync());
            return 0;
        });
    }

    [Fact]
    public async Task RestoreBuiltInsBringsBackWhatWasDeletedAndKeepsWhatIsThere()
    {
        await RemoveSeededTargetsAsync();
        await AdministerAsync(a => a.RestoreBuiltInsAsync("root", _token));
        await AdministerAsync(a => a.RestoreBuiltInsAsync("root", _token));

        Assert.True(await AllowedAsync(null, SecondRepo, null, AccessAction.Read));
        Assert.True(await AllowedAsync(User("alice"), SecondRepo, "X", AccessAction.Deploy));
        var counts = await WithAsync(async (_, _, context) => (
            await context.PermissionTargets!.CountAsync(),
            await context.PermissionGrants!.CountAsync(),
            await context.Groups!.CountAsync()));
        Assert.Equal((2, 2, 2), counts);
    }

    [Fact]
    public async Task WithoutARelationalDatabaseReadingIsOpenAndAnyAuthenticatedCallerWrites()
    {
        var repositories = new Mock<IRepositoryService>();
        var evaluator = new AccessEvaluator(new NullContext(), new AccessModelCache(new AccessModelVersion()), repositories.Object);

        Assert.True(await evaluator.IsAllowedAsync(null, Default, "X", AccessAction.Read, _token));
        Assert.False(await evaluator.IsAllowedAsync(null, Default, "X", AccessAction.Deploy, _token));
        Assert.True(await evaluator.IsAllowedAsync(User("alice"), Default, "X", AccessAction.All, _token));
    }

    [Fact]
    public async Task ACheckAgainstFiftyTargetsAndTwentyGroupsCostsMicroseconds()
    {
        await AddUsersAsync("bench");
        await AdministerAsync(async a =>
        {
            for (var g = 0; g < 20; g++) await a.CreateGroupAsync("group-" + g, null, null, _token);
            for (var t = 0; t < 50; t++)
            {
                await a.CreateTargetAsync("target-" + t, new[] { t % 2 == 0 ? "*" : "repo-" + t }, new[] { "Team" + t + ".*" }, new[] { "*.Internal" }, null, null, _token);
                await a.SetGrantAsync("target-" + t, PrincipalType.Group, await GroupKeyAsync("group-" + (t % 20)), AccessAction.Read | AccessAction.Deploy, null, _token);
                await a.SetGrantAsync("target-" + t, PrincipalType.User, "user-" + t, AccessAction.Read, null, _token);
            }

            for (var g = 0; g < 20; g++) await a.AddGroupMemberAsync("group-" + g, "bench", null, _token);
        });

        var bench = User("bench");
        using var scope = _app.Services.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();
        await evaluator.IsAllowedAsync(bench, Default, "Team7.Lib", AccessAction.Read, _token);

        const int checks = 20_000;
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < checks; i++)
        {
            await evaluator.IsAllowedAsync(bench, Default, "Team" + (i % 50) + ".Lib", AccessAction.Read, _token);
        }

        watch.Stop();
        var perCheck = watch.Elapsed.TotalMilliseconds * 1000 / checks;
        _output.WriteLine($"access check, 50 targets / 20 groups: {perCheck:F1} microseconds each");

        // Measured around 5 microseconds on a development machine; the bound is loose enough for a loaded CI machine
        // and far below the cost of the database query a request makes anyway.
        Assert.True(perCheck < 500, $"{perCheck:F1} microseconds per check");
    }
}

public class GlobSetTests
{
    [Theory]
    [InlineData("*", "Anything", true)]
    [InlineData("Contoso.*", "contoso.lib", true)]
    [InlineData("Contoso.*", "Other.Contoso.Lib", false)]
    [InlineData("*.Internal", "Team.Internal", true)]
    [InlineData("*.Internal", "Team.Internal.Extra", false)]
    [InlineData("A*B*C", "aXXbYYc", true)]
    [InlineData("A*B*C", "aXXbYY", false)]
    [InlineData("exact", "EXACT", true)]
    [InlineData("exact", "exactly", false)]
    [InlineData("", "x", false)]
    public void StarMatchesAnyRunCaseInsensitively(string pattern, string value, bool expected)
        => Assert.Equal(expected, GlobSet.Match(pattern, value));

    [Fact]
    public void AnEmptyIncludeListMatchesEverythingAndAnEmptyExcludeListNothing()
    {
        Assert.True(GlobSet.Parse(null, emptyMeansAll: true).IsMatch("X"));
        Assert.False(GlobSet.Parse(Array.Empty<string>(), emptyMeansAll: false).IsMatch("X"));
        Assert.False(GlobSet.Parse(new[] { "A.*" }, emptyMeansAll: true).IsMatch("B.Lib"));
    }
}
