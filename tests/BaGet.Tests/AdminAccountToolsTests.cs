using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// U6: the administrator creates users, lists and revokes any user's API keys, and manages organizations
/// (rename, delete, members) from <c>/admin/users</c>, <c>/admin/users/{name}</c> and <c>/admin/organizations</c>.
/// </summary>
public class AdminAccountToolsTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AdminAccountToolsTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<(BaGetApplication App, Browser Admin)> StartAsync(bool email = false)
    {
        var settings = new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        };
        if (email)
        {
            settings["Email:Smtp:Host"] = "smtp.example.com";
            settings["Email:Smtp:From"] = "baget@example.com";
            settings["Site:PublicUrl"] = "https://baget.example.com";
        }

        var app = new BaGetApplication(_output, configOverrides: settings);
        _disposables.Add(app);

        await CreateUserAsync(app, "root", administrator: true, email: email ? "root@example.com" : null);
        var admin = new Browser(app);
        _disposables.Add(admin);
        await SignInAsync(admin, "root");

        return (app, admin);
    }

    private static Task<HttpResponseMessage> PostAsync(Browser browser, string page, string handler, params (string, string)[] fields)
    {
        var form = fields.ToDictionary(f => f.Item1, f => f.Item2);
        if (!form.ContainsKey("password")) form["password"] = Password;

        return browser.PostAsync(page, form, action: $"{page}?handler={handler}");
    }

    private static string GeneratedPassword(string html)
        => WebUtility.HtmlDecode(Regex.Match(html, "<code id=\"generated-password\">(.*?)</code>").Groups[1].Value);

    // ---- create user

    [Fact]
    public async Task CreatingAUserWithAPasswordMakesAnAccountThatCanSignIn()
    {
        var (app, admin) = await StartAsync();

        using var response = await PostAsync(admin, "admin/users", "Create",
            ("newUserName", "carol"), ("newPassword", "Sup3rSecret!"), ("newEmail", "carol@example.com"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("carol was created", await admin.GetPageAsync("admin/users"));
        Assert.True(await CanSignInAsync(app, "carol", "Sup3rSecret!"));
        Assert.False((await FindAsync(app, "carol"))!.IsAdministrator);
    }

    [Fact]
    public async Task CreatingAUserWithoutAPasswordGeneratesOneShownOnce()
    {
        var (app, admin) = await StartAsync();

        using var response = await PostAsync(admin, "admin/users", "Create", ("newUserName", "carol"), ("newPassword", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var generated = GeneratedPassword(html);
        Assert.NotEmpty(generated);
        Assert.True(await CanSignInAsync(app, "carol", generated));
        Assert.DoesNotContain("generated-password", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task CreatingAUserNeedsTheAdministratorsPassword()
    {
        var (app, admin) = await StartAsync();

        using var response = await PostAsync(admin, "admin/users", "Create",
            ("newUserName", "carol"), ("newPassword", "Sup3rSecret!"), ("password", "wrong"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await FindAsync(app, "carol"));
        Assert.Contains("confirmation is incorrect", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task CreatingAUserRefusesANameTakenByAUserOrAnOrganization()
    {
        var (app, admin) = await StartAsync();
        await AddOrganizationAsync(app, "Acme", ("root", true));

        foreach (var name in new[] { "ROOT", "acme" })
        {
            using var response = await PostAsync(admin, "admin/users", "Create", ("newUserName", name), ("newPassword", "Sup3rSecret!"));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("That name is taken.", await admin.GetPageAsync("admin/users"));
        }

        Assert.Null(await FindAsync(app, "acme"));
    }

    [Fact]
    public async Task CreatingAUserRefusesAWeakPasswordAndReportsWhy()
    {
        var (app, admin) = await StartAsync();

        using var response = await PostAsync(admin, "admin/users", "Create", ("newUserName", "carol"), ("newPassword", "x"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await FindAsync(app, "carol"));
        Assert.Contains("Passwords must be at least", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task InvitingAUserMailsAResetLinkAndShowsNoPassword()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartAsync(email: true);

        using var response = await PostAsync(admin, "admin/users", "Create",
            ("newUserName", "carol"), ("newEmail", "carol@example.com"), ("invite", "true"), ("newPassword", ""));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await admin.GetPageAsync("admin/users");
        Assert.DoesNotContain("generated-password", html);
        Assert.Contains("invitation", html, StringComparison.OrdinalIgnoreCase);

        var sender = app.Services.GetRequiredService<CapturingEmailSender>();
        for (var i = 0; i < 100 && !sender.Sent.Any(m => m.Kind == "reset-link"); i++) await Task.Delay(50, cancellationToken);
        var mail = Assert.Single(sender.Sent, m => m.Kind == "reset-link");
        Assert.Equal("carol@example.com", mail.To);
        Assert.StartsWith("https://baget.example.com/account/resetpassword?", mail.Payload);
    }

    [Fact]
    public async Task AnInvitationNeedsEmailAndAnAddress()
    {
        var (app, admin) = await StartAsync(email: false);

        using var withoutSmtp = await PostAsync(admin, "admin/users", "Create",
            ("newUserName", "carol"), ("newEmail", "carol@example.com"), ("invite", "true"));
        Assert.Equal(HttpStatusCode.Redirect, withoutSmtp.StatusCode);
        Assert.Null(await FindAsync(app, "carol"));

        var (app2, admin2) = await StartAsync(email: true);
        using var withoutAddress = await PostAsync(admin2, "admin/users", "Create", ("newUserName", "carol"), ("invite", "true"));
        Assert.Equal(HttpStatusCode.Redirect, withoutAddress.StatusCode);
        Assert.Null(await FindAsync(app2, "carol"));
        Assert.Contains("email address", await admin2.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task ANonAdministratorCannotCreateAUser()
    {
        var (app, _) = await StartAsync();
        await CreateUserAsync(app, "alice");
        using var alice = new Browser(app);
        await SignInAsync(alice, "alice");

        using var response = await alice.PostAsync("account/settings", new Dictionary<string, string>
        {
            { "newUserName", "carol" }, { "newPassword", "Sup3rSecret!" }, { "password", Password },
        }, action: "admin/users?handler=Create");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await FindAsync(app, "carol"));
    }

    // ---- user detail and API keys

    [Fact]
    public async Task UserDetailShowsTheAccountItsKeysAndItsOrganizations()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice", email: "alice@example.com");
        await AddOrganizationAsync(app, "Acme", ("alice", true));
        await WithAsync(app, async p =>
        {
            await p.GetRequiredService<ApiKeyService>().CreateAsync(
                (await FindAsync(app, "alice"))!.Id, "ci key", new[] { ApiKeyScopes.Push }, "Contoso.*", null, CancellationToken.None);
            return true;
        });

        var html = await admin.GetPageAsync("admin/users/alice");

        Assert.Contains("alice@example.com", html);
        Assert.Contains("ci key", html);
        Assert.Contains("Contoso.*", html);
        Assert.Contains("Acme", html);
        Assert.Contains("/admin/users/alice/packages", html);
        Assert.Contains("id=\"user-section-keys\"", html);
        Assert.Contains("id=\"user-section-organizations\"", html);
    }

    [Fact]
    public async Task UserDetailOfAnUnknownUserIsNotFoundAndNonAdministratorsDoNotSeeIt()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        using var alice = new Browser(app);
        await SignInAsync(alice, "alice");

        using var unknown = await admin.GetAsync("admin/users/nobody");
        using var denied = await alice.GetAsync("admin/users/alice");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    private static async Task<int> AddKeyAsync(BaGetApplication app, string userName)
        => await WithAsync(app, async p =>
        {
            var user = (await FindAsync(app, userName))!;
            var keys = p.GetRequiredService<ApiKeyService>();
            await keys.CreateAsync(user.Id, "k", new[] { ApiKeyScopes.Push }, "*", null, CancellationToken.None);
            return (await keys.ListAsync(user.Id, CancellationToken.None)).Last().Key;
        });

    private static async Task<int> KeyCountAsync(BaGetApplication app, string userName)
    {
        var user = (await FindAsync(app, userName))!;

        return (await WithAsync(app, p => p.GetRequiredService<ApiKeyService>().ListAsync(user.Id, CancellationToken.None))).Count;
    }

    [Fact]
    public async Task AnAdministratorRevokesAnotherUsersKey()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        var key = await AddKeyAsync(app, "alice");

        using var response = await PostAsync(admin, "admin/users/alice", "RevokeKey", ("key", key.ToString()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, await KeyCountAsync(app, "alice"));
    }

    [Fact]
    public async Task RevokingNeedsThePasswordAndOnlyTouchesThatUsersKeys()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        var bobKey = await AddKeyAsync(app, "bob");
        await AddKeyAsync(app, "alice");

        using var wrong = await PostAsync(admin, "admin/users/bob", "RevokeKey", ("key", bobKey.ToString()), ("password", "wrong"));
        Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        using var foreign = await PostAsync(admin, "admin/users/alice", "RevokeKey", ("key", bobKey.ToString()));
        Assert.Equal(HttpStatusCode.Redirect, foreign.StatusCode);

        Assert.Equal(1, await KeyCountAsync(app, "bob"));
        Assert.Equal(1, await KeyCountAsync(app, "alice"));
    }

    // ---- organizations

    private static Task<int> OwnedByAsync(BaGetApplication app, string owner)
        => WithAsync(app, p => p.GetRequiredService<IContext>().PackageOwners!
            .IgnoreQueryFilters()
            .CountAsync(o => o.OwnerName == owner));

    private static async Task AddOwnerInAsync(BaGetApplication app, string repository, string packageId, string owner)
    {
        await app.CreateRepositoryAsync(repository);
        using var scope = await app.CreateRepositoryScopeAsync(repository);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        context.PackageOwners!.Add(new PackageOwner { PackageId = packageId, OwnerName = owner });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OrganizationsListsEveryOrganizationWithMembersAndPackages()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        await AddOrganizationAsync(app, "Acme", ("alice", true), ("bob", false));
        await AddOrganizationAsync(app, "Globex", ("bob", true));
        await AddOwnerAsync(app, "Contoso.Lib", "Acme");

        var html = await admin.GetPageAsync("admin/organizations");

        Assert.Contains("/admin/organizations/Acme", html);
        Assert.Contains("/admin/organizations/Globex", html);
        Assert.Matches("(?s)Acme.*?<td[^>]*>2</td>", html);
    }

    [Fact]
    public async Task RenamingAnOrganizationUpdatesOwnersInEveryRepositoryAtOnce()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await AddOrganizationAsync(app, "Acme", ("alice", true));
        await AddOwnerAsync(app, "Contoso.Lib", "Acme");
        await AddOwnerAsync(app, "Other.Lib", "alice");
        await AddOwnerInAsync(app, "second", "Contoso.Second", "ACME");

        using var response = await PostAsync(admin, "admin/organizations/Acme", "Rename", ("newName", "Initech"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, await OwnedByAsync(app, "Acme") + await OwnedByAsync(app, "ACME"));
        Assert.Equal(2, await OwnedByAsync(app, "Initech"));
        Assert.Equal(1, await OwnedByAsync(app, "alice"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("admin/organizations/Acme")).StatusCode);
        Assert.Contains("Initech", await admin.GetPageAsync("admin/organizations/Initech"));
    }

    [Fact]
    public async Task RenamingRefusesANameTakenByAUserOrAnotherOrganizationButAllowsACaseChange()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await AddOrganizationAsync(app, "Acme", ("alice", true));
        await AddOrganizationAsync(app, "Globex", ("alice", true));

        foreach (var name in new[] { "ALICE", "globex" })
        {
            using var refused = await PostAsync(admin, "admin/organizations/Acme", "Rename", ("newName", name));
            Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
            Assert.Contains("That name is taken.", await admin.GetPageAsync("admin/organizations/Acme"));
        }

        using var blank = await PostAsync(admin, "admin/organizations/Acme", "Rename", ("newName", "  "));
        Assert.Equal(HttpStatusCode.Redirect, blank.StatusCode);
        using var slash = await PostAsync(admin, "admin/organizations/Acme", "Rename", ("newName", "a/b"));
        Assert.Equal(HttpStatusCode.Redirect, slash.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("admin/organizations/Acme")).StatusCode);

        using var caseOnly = await PostAsync(admin, "admin/organizations/Acme", "Rename", ("newName", "ACME"));
        Assert.Equal(HttpStatusCode.Redirect, caseOnly.StatusCode);
        Assert.Contains("ACME", await admin.GetPageAsync("admin/organizations/ACME"));
    }

    [Fact]
    public async Task RenamingNeedsThePassword()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await AddOrganizationAsync(app, "Acme", ("alice", true));

        using var response = await PostAsync(admin, "admin/organizations/Acme", "Rename", ("newName", "Initech"), ("password", "wrong"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("admin/organizations/Acme")).StatusCode);
    }

    [Fact]
    public async Task DeletingAnOrganizationIsRefusedWhileItOwnsPackagesInAnyRepository()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await AddOrganizationAsync(app, "Acme", ("alice", true));
        await AddOwnerInAsync(app, "second", "Contoso.Second", "Acme");

        using var response = await PostAsync(admin, "admin/organizations/Acme", "Delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("owns packages", await admin.GetPageAsync("admin/organizations/Acme"));
    }

    [Fact]
    public async Task DeletingAnOrganizationThatOwnsNothingRemovesItAndItsMemberships()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await AddOrganizationAsync(app, "Acme", ("alice", true));

        using var wrong = await PostAsync(admin, "admin/organizations/Acme", "Delete", ("password", "wrong"));
        Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("admin/organizations/Acme")).StatusCode);

        using var response = await PostAsync(admin, "admin/organizations/Acme", "Delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("admin/organizations/Acme")).StatusCode);
        Assert.Empty(await WithAsync(app, p => p.GetRequiredService<PackageOwnershipService>().GetMembersAsync("Acme", CancellationToken.None)));
        Assert.Equal(0, await WithAsync(app, p => p.GetRequiredService<IContext>().OrganizationMembers!.CountAsync()));
    }

    [Fact]
    public async Task MembersAreAddedPromotedAndRemovedKeepingAnAdministrator()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await CreateUserAsync(app, "bob");
        await AddOrganizationAsync(app, "Acme", ("alice", true));

        using var add = await PostAsync(admin, "admin/organizations/Acme", "AddMember", ("userName", "bob"));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        Assert.Equal(2, (await WithAsync(app, p => p.GetRequiredService<PackageOwnershipService>().GetMembersAsync("Acme", CancellationToken.None))).Count);

        using var unknown = await PostAsync(admin, "admin/organizations/Acme", "AddMember", ("userName", "nobody"));
        Assert.Contains("There is no user with that name.", await admin.GetPageAsync("admin/organizations/Acme"));

        using var removeLast = await PostAsync(admin, "admin/organizations/Acme", "RemoveMember", ("userName", "alice"));
        Assert.Contains("at least one administrator", await admin.GetPageAsync("admin/organizations/Acme"));
        using var demoteLast = await PostAsync(admin, "admin/organizations/Acme", "SetAdmin", ("userName", "alice"), ("isAdmin", "false"));
        Assert.Contains("at least one administrator", await admin.GetPageAsync("admin/organizations/Acme"));

        using var promote = await PostAsync(admin, "admin/organizations/Acme", "SetAdmin", ("userName", "bob"), ("isAdmin", "true"));
        using var remove = await PostAsync(admin, "admin/organizations/Acme", "RemoveMember", ("userName", "alice"));

        var members = await WithAsync(app, p => p.GetRequiredService<PackageOwnershipService>().GetMembersAsync("Acme", CancellationToken.None));
        var only = Assert.Single(members);
        Assert.True(only.IsAdmin);
        Assert.Equal((await FindAsync(app, "bob"))!.Id, only.UserId);
    }

    [Fact]
    public async Task OrganizationPagesAreAdministratorOnly()
    {
        var (app, _) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await AddOrganizationAsync(app, "Acme", ("alice", true));
        using var alice = new Browser(app);
        await SignInAsync(alice, "alice");

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("admin/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("admin/organizations/Acme")).StatusCode);
    }

    [Fact]
    public async Task WritesAreRefusedWhileTheServerIsClosedForMaintenance()
    {
        var (app, admin) = await StartAsync();
        await CreateUserAsync(app, "alice");
        await AddOrganizationAsync(app, "Acme", ("alice", true));
        app.Services.GetRequiredService<MaintenanceGate>().CloseUntilRestart("test");

        using var rename = await PostAsync(admin, "admin/organizations/Acme", "Rename", ("newName", "Initech"));
        using var create = await PostAsync(admin, "admin/users", "Create", ("newUserName", "carol"), ("newPassword", "Sup3rSecret!"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, rename.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
    }
}
