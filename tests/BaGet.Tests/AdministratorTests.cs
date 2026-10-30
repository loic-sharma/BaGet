using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Models.Users;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The administrator role: who may reach the administrator pages, promoting and demoting,
/// and the rules that keep at least one administrator.
/// </summary>
public class AdministratorTests : IDisposable
{
    private const string Password = "Passw0rd!";
    private const int UsersPageSize = UsersViewModel.PageSize;

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AdministratorTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private (BaGetApplication App, Browser Browser) Start(bool accounts = true, string apiKey = "")
    {
        var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", accounts ? "true" : "false" },
            { "ApiKey", apiKey },
        });
        _disposables.Add(app);

        return (app, NewBrowser(app));
    }

    private Browser NewBrowser(BaGetApplication app)
    {
        var browser = new Browser(app);
        _disposables.Add(browser);

        return browser;
    }

    private static async Task<T> WithAsync<T>(BaGetApplication app, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = app.Services.CreateScope();

        return await action(scope.ServiceProvider);
    }

    private static Task<AdministratorChange> PromoteAsync(BaGetApplication app, string? userName)
        => WithAsync(app, p => p.GetRequiredService<AdministratorService>().PromoteAsync(userName, "test", CancellationToken.None));

    private static Task<AdministratorChange> DemoteAsync(BaGetApplication app, string? userName)
        => WithAsync(app, p => p.GetRequiredService<AdministratorService>().DemoteAsync(userName, "test", CancellationToken.None));

    #pragma warning disable CS8602
    private static async Task<bool> IsAdministratorAsync(BaGetApplication app, string userName)
        => (await WithAsync(app, p => p.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync(userName))).IsAdministrator;
    #pragma warning restore CS8602

    private static Task<HttpResponseMessage> PostUsersAsync(Browser browser, string handler, string userName, string password = Password)
        => browser.PostAsync("admin/users", new Dictionary<string, string>
        {
            { "userName", userName },
            { "password", password },
        }, action: $"admin/users?handler={handler}", tokenPage: "account/settings");

    private async Task<(BaGetApplication App, Browser Admin)> StartWithAdministratorAsync()
    {
        var (app, admin) = Start();
        await admin.RegisterAsync("root");
        await PromoteAsync(app, "root");

        return (app, admin);
    }

    // --- Access -------------------------------------------------------------------------

    [Theory]
    [InlineData("admin/users")]
    [InlineData("admin/settings")]
    public async Task AdministratorPagesAreNotFoundWhileAccountsAreDisabled(string url)
    {
        var (_, browser) = Start(accounts: false);

        using var response = await browser.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("admin/users")]
    [InlineData("admin/settings")]
    public async Task ANonAdministratorGetsNotFound(string url)
    {
        var (_, browser) = Start();
        await browser.RegisterAsync("alice");

        using var response = await browser.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnAnonymousVisitorIsAskedToSignIn()
    {
        var (_, browser) = Start();

        using var response = await browser.GetAsync("admin/users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        #pragma warning disable CS8602
        Assert.Contains("account/login", response.Headers.Location.ToString());
        #pragma warning restore CS8602
    }

    [Fact]
    public async Task AnAdministratorSeesTheUsersPage()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");

        var html = await admin.GetPageAsync("admin/users");

        Assert.Contains("alice", html);
        Assert.Contains("root", html);
        Assert.Contains("label-info\">Administrator", html);
    }

    [Fact]
    public async Task TheUsersPageFiltersByNameAndClampsThePage()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");
        using var third = NewBrowser(app);
        await third.RegisterAsync("bob");

        var filtered = await admin.GetPageAsync("admin/users?q=ALI");

        Assert.Contains("alice", filtered);
        Assert.DoesNotContain(">bob<", filtered);

        // A page beyond the end shows the last one instead of an empty page.
        Assert.Contains("alice", await admin.GetPageAsync("admin/users?p=99"));
    }

    [Fact]
    public async Task TheUsersPageShowsLaterPagesAndKeepsThePageInItsLinks()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        await WithAsync(app, async p =>
        {
            var context = p.GetRequiredService<IContext>();
            for (var i = 0; i < UsersPageSize; i++)
            {
                context.Users.Add(new BaGetUser { Id = $"id{i}", UserName = $"amber{i:D3}", NormalizedUserName = $"AMBER{i:D3}", Created = DateTime.UtcNow });
            }

            await context.SaveChangesAsync(CancellationToken.None);

            return true;
        });

        // "root" plus 50 "amberNNN" names: root sorts after them, so it is on page 2 only.
        var first = await admin.GetPageAsync("admin/users");
        var second = await admin.GetPageAsync("admin/users?p=2");

        Assert.Contains("amber000", first);
        Assert.DoesNotContain(">root</a></td>", first);
        Assert.Contains("p=2", first);
        Assert.Contains(">root</a></td>", second);
        Assert.DoesNotContain("amber000", second);
    }

    [Fact]
    public async Task InitialAdministratorsDoNotPromoteOnceAnAdministratorExists()
    {
        var (app, admin) = await StartWithAdministratorAsync();

        // The listed name is registered only after the server already has an administrator.
        using var late = NewBrowser(app);
        await late.RegisterAsync("alice");
        await WithAsync(app, async p =>
        {
            await p.GetRequiredService<AdministratorService>()
                .PromoteInitialAdministratorsAsync("alice;root", CancellationToken.None);

            return true;
        });

        Assert.False(await IsAdministratorAsync(app, "alice"));
        Assert.True(await IsAdministratorAsync(app, "root"));
    }

    [Fact]
    public async Task InitialAdministratorsDoNotReverseADemotion()
    {
        var (app, _) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");
        await PromoteAsync(app, "alice");
        await DemoteAsync(app, "root");

        await WithAsync(app, async p =>
        {
            await p.GetRequiredService<AdministratorService>()
                .PromoteInitialAdministratorsAsync("root", CancellationToken.None);

            return true;
        });

        Assert.False(await IsAdministratorAsync(app, "root"));
    }

    [Fact]
    public async Task TheMenuLinksAreShownOnlyToAdministrators()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var user = NewBrowser(app);
        await user.RegisterAsync("alice");

        var adminHtml = await admin.GetPageAsync("stats");
        var userHtml = await user.GetPageAsync("stats");

        Assert.Contains("admin/users", adminHtml);
        Assert.Contains("Server settings", adminHtml);
        Assert.DoesNotContain("admin/users", userHtml);
        Assert.DoesNotContain("Server settings", userHtml);
    }

    [Fact]
    public async Task ADemotionTakesEffectOnTheNextRequest()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var second = NewBrowser(app);
        await second.RegisterAsync("alice");
        await PromoteAsync(app, "alice");
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("admin/users")).StatusCode);

        using var demotion = await PostUsersAsync(admin, "Demote", "alice");
        Assert.Equal(HttpStatusCode.Redirect, demotion.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync("admin/users")).StatusCode);
    }

    // --- Settings page ------------------------------------------------------------------

    private static Task<HttpResponseMessage> PostSettingsAsync(Browser browser, Dictionary<string, string> fields, string password = Password, string section = "")
    {
        fields["section"] = section;
        fields["password"] = password;

        return browser.PostAsync("admin/settings", fields);
    }

    private static async Task<HttpStatusCode> PushAsync(BaGetApplication app, string? apiKey)
    {
        using var client = app.CreateClient();
        using var package = TestPackages.Build("Settings.Package", "1.0.0");
        using var request = TestPackages.PushRequest(package, apiKey);
        using var response = await client.SendAsync(request);

        return response.StatusCode;
    }

    [Fact]
    public async Task AnAdministratorSeesTheSettingsPage()
    {
        var (_, admin) = await StartWithAdministratorAsync();

        var html = await admin.GetPageAsync("admin/settings");

        Assert.Contains("Server settings", html);
        Assert.Contains("AllowPackageOverwrites", html);
        Assert.Contains("name=\"v:AllowPackageOverwrites\"", html);
        Assert.Contains("appsettings.Overrides.json", html);
    }

    [Fact]
    public async Task SavingASettingWritesTheOverridesFileAndAppliesWithoutARestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string key = "secret-push-key";
        var (app, admin) = Start(apiKey: key);
        await admin.RegisterAsync("root");
        await PromoteAsync(app, "root");

        Assert.Equal(HttpStatusCode.Created, await PushAsync(app, key));
        Assert.Equal(HttpStatusCode.Conflict, await PushAsync(app, key));

        using var response = await PostSettingsAsync(admin, new Dictionary<string, string> { { "v:AllowPackageOverwrites", "true" } });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var file = app.Services.GetRequiredService<SettingsOverridesStore>().FilePath;
        Assert.Contains("\"AllowPackageOverwrites\": true", await File.ReadAllTextAsync(file, cancellationToken));
        Assert.Equal(HttpStatusCode.Created, await PushAsync(app, key));

        var html = await admin.GetPageAsync("admin/settings");
        Assert.Contains("Saved 1 setting(s).", html);
        Assert.Contains("Saved override", html);
    }

    [Fact]
    public async Task SavingASettingNeedsThePassword()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartWithAdministratorAsync();

        var html = await (await PostSettingsAsync(admin, new Dictionary<string, string> { { "v:AllowPackageOverwrites", "true" } }, password: "wrong")).Content.ReadAsStringAsync(cancellationToken);

        Assert.Contains("confirmation is incorrect", html);
        Assert.False(File.Exists(app.Services.GetRequiredService<SettingsOverridesStore>().FilePath));
    }

    [Fact]
    public async Task AnInvalidValueIsReportedInItsSection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartWithAdministratorAsync();

        var html = await (await PostSettingsAsync(admin, new Dictionary<string, string> { { "v:Mirror:PackageDownloadTimeoutSeconds", "abc" } }, section: "Mirror")).Content.ReadAsStringAsync(cancellationToken);

        Assert.Contains("Mirror:PackageDownloadTimeoutSeconds: ", html);
        Assert.Contains("alert-danger", html);
        Assert.False(File.Exists(app.Services.GetRequiredService<SettingsOverridesStore>().FilePath));
    }

    [Fact]
    public async Task ANonAdministratorCannotPostSettings()
    {
        var (app, _) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");

        using var response = await other.PostAsync(
            "admin/settings",
            new Dictionary<string, string> { { "section", "" }, { "password", Password }, { "v:AllowPackageOverwrites", "true" } },
            tokenPage: "account/settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(File.Exists(app.Services.GetRequiredService<SettingsOverridesStore>().FilePath));
    }

    [Fact]
    public async Task ASecretValueNeverAppearsInThePage()
    {
        const string secret = "very-secret-api-key-value";
        var (app, admin) = Start(apiKey: secret);
        await admin.RegisterAsync("root");
        await PromoteAsync(app, "root");

        var html = await admin.GetPageAsync("admin/settings");

        Assert.DoesNotContain(secret, html);
        Assert.Contains("unchanged", html);
    }

    [Fact]
    public async Task ASecretIsClearedExplicitlyAndKeptWhenBlank()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = Start(apiKey: "kept-key");
        await admin.RegisterAsync("root");
        await PromoteAsync(app, "root");
        var path = app.Services.GetRequiredService<SettingsOverridesStore>().FilePath;

        await PostSettingsAsync(admin, new Dictionary<string, string> { { "v:ApiKey", "" } });
        Assert.False(File.Exists(path));

        await PostSettingsAsync(admin, new Dictionary<string, string> { { "clear:ApiKey", "true" } });
        Assert.Contains("\"ApiKey\": \"\"", await File.ReadAllTextAsync(path, cancellationToken));
    }

    // --- Promote and demote -------------------------------------------------------------

    [Fact]
    public async Task AnAdministratorPromotesAUserWithTheirPassword()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");

        using var response = await PostUsersAsync(admin, "Promote", "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await IsAdministratorAsync(app, "alice"));
        Assert.Contains("alice is now an administrator.", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task APromotionWithAWrongPasswordIsRefused()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");

        using var response = await PostUsersAsync(admin, "Promote", "alice", password: "wrong");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await IsAdministratorAsync(app, "alice"));
        Assert.Contains("confirmation is incorrect", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task PromotingAnUnknownUserReportsIt()
    {
        var (_, admin) = await StartWithAdministratorAsync();

        using var response = await PostUsersAsync(admin, "Promote", "nobody");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("There is no user with that name.", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task ANonAdministratorCannotPostPromote()
    {
        var (app, _) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");

        using var response = await PostUsersAsync(other, "Promote", "alice");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await IsAdministratorAsync(app, "alice"));
    }

    [Fact]
    public async Task TheLastAdministratorCannotBeDemoted()
    {
        var (app, admin) = await StartWithAdministratorAsync();

        using var response = await PostUsersAsync(admin, "Demote", "root");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await IsAdministratorAsync(app, "root"));
        Assert.Contains("last administrator cannot be demoted", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task AnAdministratorCanBeDemotedWhileAnotherRemains()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");
        await PromoteAsync(app, "alice");

        using var response = await PostUsersAsync(admin, "Demote", "root");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await IsAdministratorAsync(app, "root"));
        Assert.True(await IsAdministratorAsync(app, "alice"));
    }

    [Fact]
    public async Task ThePasswordIsRequiredToDemoteToo()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");
        await PromoteAsync(app, "alice");

        using var response = await PostUsersAsync(admin, "Demote", "alice", password: "");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await IsAdministratorAsync(app, "alice"));
    }

    // --- Deleting the account -----------------------------------------------------------

    [Fact]
    public async Task TheLastAdministratorCannotDeleteTheirAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, admin) = await StartWithAdministratorAsync();

        using var response = await admin.PostAsync("account/settings", new Dictionary<string, string>
        {
            { "password", Password },
        }, action: "account/settings?handler=Delete");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("You are the last administrator. Promote another user first.", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.NotNull(await WithAsync(app, p => p.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync("root")));
    }

    [Fact]
    public async Task AnAdministratorCanDeleteTheirAccountWhileAnotherRemains()
    {
        var (app, admin) = await StartWithAdministratorAsync();
        using var other = NewBrowser(app);
        await other.RegisterAsync("alice");
        await PromoteAsync(app, "alice");

        using var response = await admin.PostAsync("account/settings", new Dictionary<string, string>
        {
            { "password", Password },
        }, action: "account/settings?handler=Delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await WithAsync(app, p => p.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync("root")));
    }

    // --- The service --------------------------------------------------------------------

    private static Task<bool> CreateDefaultAdministratorAsync(BaGetApplication app)
        => WithAsync(app, p => p.GetRequiredService<AdministratorService>().CreateDefaultAdministratorAsync(CancellationToken.None));

    private static async Task<HttpStatusCode> SignInAsDefaultAdministratorAsync(Browser browser)
    {
        using var login = await browser.PostAsync("account/login", new Dictionary<string, string>
        {
            { "UserName", AdministratorService.DefaultAdministratorUserName },
            { "Password", AdministratorService.DefaultAdministratorPassword },
        });

        return login.StatusCode;
    }

    [Fact]
    public async Task AServerWithNoUsersGetsTheDefaultAdministrator()
    {
        var (app, browser) = Start();

        Assert.True(await CreateDefaultAdministratorAsync(app));
        Assert.False(await CreateDefaultAdministratorAsync(app));

        Assert.True(await IsAdministratorAsync(app, AdministratorService.DefaultAdministratorUserName));
        Assert.True(await WithAsync(app, p => p.GetRequiredService<AdministratorService>().HasDefaultPasswordAsync()));
        Assert.Equal(HttpStatusCode.Redirect, await SignInAsDefaultAdministratorAsync(browser));
        Assert.Contains("admin/users", await browser.GetPageAsync("stats"));
    }

    [Fact]
    public async Task TheDefaultAdministratorIsNotCreatedOnceAUserExists()
    {
        var (app, browser) = Start();
        await browser.RegisterAsync("alice");

        Assert.False(await CreateDefaultAdministratorAsync(app));
        Assert.Null(await WithAsync(app, p => p.GetRequiredService<UserManager<BaGetUser>>()
            .FindByNameAsync(AdministratorService.DefaultAdministratorUserName)));
        Assert.False(await WithAsync(app, p => p.GetRequiredService<AdministratorService>().HasDefaultPasswordAsync()));
    }

    /// <summary>
    /// With SMTP configured, sign-in needs a confirmed email. The default administrator has no address, so it
    /// is created confirmed; otherwise a fresh install with SMTP could never be signed in to.
    /// </summary>
    [Fact]
    public async Task TheDefaultAdministratorCanSignInWhileSmtpIsConfigured()
    {
        var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
            { "Site:PublicUrl", "https://baget.example.com" },
        });
        _disposables.Add(app);

        Assert.True(await CreateDefaultAdministratorAsync(app));
        Assert.Equal(HttpStatusCode.Redirect, await SignInAsDefaultAdministratorAsync(NewBrowser(app)));
    }

    [Fact]
    public async Task InitialAdministratorsAreOnlyPromotedWhenTheyExist()
    {
        var (app, browser) = Start();
        await browser.RegisterAsync("alice");
        using var other = NewBrowser(app);
        await other.RegisterAsync("bob");

        await WithAsync(app, async p =>
        {
            await p.GetRequiredService<AdministratorService>()
                .PromoteInitialAdministratorsAsync(" alice ; ghost;;", CancellationToken.None);

            return true;
        });

        Assert.True(await IsAdministratorAsync(app, "alice"));
        Assert.False(await IsAdministratorAsync(app, "bob"));
        Assert.Null(await WithAsync(app, p => p.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync("ghost")));
    }

    [Fact]
    public async Task InitialAdministratorsToleratesNoNames()
    {
        var (app, _) = Start();

        await WithAsync(app, async p =>
        {
            await p.GetRequiredService<AdministratorService>().PromoteInitialAdministratorsAsync(null, CancellationToken.None);

            return true;
        });
    }

    [Fact]
    public async Task ThePromoteAndDemoteResultsDescribeWhatHappened()
    {
        var (app, browser) = Start();
        await browser.RegisterAsync("alice");

        Assert.Equal(AdministratorChange.UserNotFound, await PromoteAsync(app, "ghost"));
        Assert.Equal(AdministratorChange.UserNotFound, await DemoteAsync(app, "ghost"));
        Assert.Equal(AdministratorChange.Changed, await PromoteAsync(app, "alice"));
        Assert.Equal(AdministratorChange.Unchanged, await PromoteAsync(app, "alice"));
        Assert.Equal(AdministratorChange.LastAdministrator, await DemoteAsync(app, "alice"));
    }

    [Fact]
    public async Task AccountsDisabledRegistersNoAdministratorService()
    {
        var (app, _) = Start(accounts: false);

        await WithAsync(app, p =>
        {
            Assert.Null(p.GetService<AdministratorService>());

            return Task.FromResult(true);
        });
    }
}
