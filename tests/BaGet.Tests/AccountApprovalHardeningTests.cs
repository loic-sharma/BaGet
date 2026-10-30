using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Extensions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// <c>Accounts:RequireApproval</c> hardening: password guessing against a pending account is counted and locked out,
/// promotion approves, registration and email confirmation tell the user about the approval, npm login / the OCI token
/// refuse a pending user, the external provider, the detail page, the REST field and the read-only gate.
/// </summary>
public class AccountApprovalHardeningTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AccountApprovalHardeningTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<(BaGetApplication App, Browser Admin)> StartAsync(Dictionary<string, string>? extra = null, Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "Accounts:RequireApproval", "true" },
            { "ApiKey", "" },
        };
        if (extra != null)
        {
            foreach (var pair in extra) settings[pair.Key] = pair.Value;
        }

        var app = new BaGetApplication(_output, configOverrides: settings, configureServices: configureServices);
        _disposables.Add(app);

        await CreateUserAsync(app, "root", administrator: true, email: "root@example.com");
        var admin = new Browser(app);
        _disposables.Add(admin);
        await SignInAsync(admin, "root");

        return (app, admin);
    }

    /// <summary>A pending account created straight in the database (what registration does).</summary>
    private static async Task CreatePendingAsync(BaGetApplication app, string name)
    {
        using var scope = app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().CreateAsync(
            new BaGetUser { UserName = name, Created = DateTime.UtcNow, PendingApproval = true }, Password);
        Assert.True(result.Succeeded);
    }

    private static async Task<string> TryLoginAsync(BaGetApplication app, string name, string password)
    {
        using var browser = new Browser(app);
        using var response = await browser.PostAsync("account/login", new Dictionary<string, string> { { "UserName", name }, { "Password", password } });

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static string PathOf(string link) => new Uri(link).PathAndQuery.TrimStart('/');

    // ---- 1: guessing a pending account's password ----

    [Fact]
    public async Task WrongPasswordsAgainstAPendingAccountLockItOutAndTheRightOneNoLongerAnswers()
    {
        var (app, _) = await StartAsync();
        await CreatePendingAsync(app, "carol");

        string page = string.Empty;
        for (var i = 0; i < 12 && !page.Contains("locked"); i++) page = await TryLoginAsync(app, "carol", "Wr0ng-password" + i);

        Assert.Contains("locked", page);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            Assert.True(await users.IsLockedOutAsync((await users.FindByNameAsync("carol"))!));
        }

        // Locked out: the right password reveals nothing (no "waiting for an administrator" oracle).
        var right = await TryLoginAsync(app, "carol", Password);
        Assert.DoesNotContain("waiting for an administrator", right);
        Assert.Contains("locked", right);
    }

    [Fact]
    public async Task TheRightPasswordOfAPendingAccountStillGetsThePendingMessageWhileNotLockedOut()
    {
        var (app, _) = await StartAsync();
        await CreatePendingAsync(app, "carol");

        Assert.Contains("incorrect", await TryLoginAsync(app, "carol", "Wr0ng-password"));
        Assert.Contains("waiting for an administrator", await TryLoginAsync(app, "carol", Password));
    }

    [Fact]
    public async Task AFailedGuessAgainstAPendingAccountIsARefusedWriteWhileTheGateIsClosed()
    {
        var (app, _) = await StartAsync();
        await CreatePendingAsync(app, "carol");
        app.Services.GetRequiredService<MaintenanceGate>().CloseUntilRestart("test");

        using var browser = new Browser(app);
        using var response = await browser.PostAsync("account/login", new Dictionary<string, string> { { "UserName", "carol" }, { "Password", "Wr0ng-password" } });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ---- 2: promotion approves ----

    [Fact]
    public async Task PromotingAPendingAccountApprovesIt()
    {
        var (app, admin) = await StartAsync();
        await CreatePendingAsync(app, "carol");

        using var scope = app.Services.CreateScope();
        var change = await scope.ServiceProvider.GetRequiredService<AdministratorService>().PromoteAsync("carol", "test", TestContext.Current.CancellationToken);

        Assert.Equal(AdministratorChange.Changed, change);
        var carol = (await FindAsync(app, "carol"))!;
        Assert.True(carol.IsAdministrator);
        Assert.False(carol.PendingApproval);
        Assert.True(await CanSignInAsync(app, "carol", Password));
        Assert.DoesNotContain("Awaiting approval", await admin.GetPageAsync("admin/users"));
    }

    [Fact]
    public async Task PromotingAnAdministratorWhoIsStillPendingApprovesItToo()
    {
        var (app, _) = await StartAsync();
        await CreatePendingAsync(app, "carol");
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var carol = (await users.FindByNameAsync("carol"))!;
            carol.IsAdministrator = true; // what an older promote left behind
            Assert.True((await users.UpdateAsync(carol)).Succeeded);
        }

        using (var scope = app.Services.CreateScope())
        {
            Assert.Equal(AdministratorChange.Changed, await scope.ServiceProvider.GetRequiredService<AdministratorService>().PromoteAsync("carol", "test", TestContext.Current.CancellationToken));
        }

        Assert.False((await FindAsync(app, "carol"))!.PendingApproval);
    }

    [Fact]
    public async Task ThePromoteButtonOnTheUsersPageApprovesToo()
    {
        var (app, admin) = await StartAsync();
        await CreatePendingAsync(app, "carol");

        using var response = await admin.PostAsync("admin/users", new Dictionary<string, string> { { "userName", "carol" }, { "password", Password } },
            action: "admin/users?handler=Promote");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False((await FindAsync(app, "carol"))!.PendingApproval);
    }

    [Fact]
    public async Task TheApprovalServiceApprovesByName()
    {
        var (app, _) = await StartAsync();
        await CreatePendingAsync(app, "carol");
        await CreateUserAsync(app, "dave");

        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountApprovalService>();

        Assert.Equal(AccountApprovalResult.NotPending, await service.ApproveAsync("dave", "cli", TestContext.Current.CancellationToken));
        Assert.Equal(AccountApprovalResult.UserNotFound, await service.ApproveAsync("nobody", "cli", TestContext.Current.CancellationToken));
        Assert.Equal(AccountApprovalResult.Approved, await service.ApproveAsync("carol", "cli", TestContext.Current.CancellationToken));
        Assert.Equal(AccountApprovalResult.NotPending, await service.ApproveAsync("carol", "cli", TestContext.Current.CancellationToken));
    }

    // ---- 3: registration with email, confirmation ----

    [Fact]
    public async Task RegisteringWithEmailConfiguredTellsTheUserApprovalIsNeededToo()
    {
        var (app, _) = await StartAsync(new Dictionary<string, string>
        {
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
            { "Site:PublicUrl", "https://baget.example.com" },
        });

        using var browser = new Browser(app);
        using var response = await browser.PostAsync("account/register", new Dictionary<string, string>
        {
            { "UserName", "carol" }, { "Email", "carol@example.com" }, { "Password", Password }, { "ConfirmPassword", Password },
        });
        var page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("An administrator must also enable your account", page);

        // Following the emailed link confirms the address and repeats that approval is still needed.
        var mail = Assert.Single(app.Services.GetRequiredService<CapturingEmailSender>().Sent);
        using var followed = await browser.GetAsync(PathOf(mail.Payload));
        var confirmed = await followed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("confirmed", confirmed);
        Assert.Contains("id=\"confirm-awaiting-approval\"", confirmed);
        Assert.True((await FindAsync(app, "carol"))!.PendingApproval);
    }

    [Fact]
    public async Task ConfirmingTheEmailOfAnApprovedAccountSaysNothingAboutApproval()
    {
        var (app, _) = await StartAsync(new Dictionary<string, string>
        {
            { "Accounts:RequireApproval", "false" },
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
            { "Site:PublicUrl", "https://baget.example.com" },
        });

        using var browser = new Browser(app);
        using (await browser.PostAsync("account/register", new Dictionary<string, string>
        {
            { "UserName", "carol" }, { "Email", "carol@example.com" }, { "Password", Password }, { "ConfirmPassword", Password },
        })) { }

        var mail = Assert.Single(app.Services.GetRequiredService<CapturingEmailSender>().Sent);
        using var followed = await browser.GetAsync(PathOf(mail.Payload));

        Assert.DoesNotContain("confirm-awaiting-approval", await followed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // ---- 4 + 7: npm login and the OCI token ----

    [Fact]
    public async Task NpmLoginOfAPendingUserIsRefusedWithoutLeakingExistenceAndCountsGuesses()
    {
        var (app, _) = await StartAsync();
        await app.CreateNpmRepositoryAsync("web", cancellationToken: TestContext.Current.CancellationToken);
        await CreatePendingAsync(app, "carol");
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        async Task<(HttpStatusCode Status, string Body)> LoginAsync(string user, string password)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/r/web/npm/-/user/org.couchdb.user:" + user)
            {
                Content = new StringContent($$"""{"_id":"org.couchdb.user:{{user}}","name":"{{user}}","password":"{{password}}","type":"user"}""", Encoding.UTF8, "application/json"),
            };
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // A wrong password gets the generic answer, the same as for an unknown user.
        var wrong = await LoginAsync("carol", "Wr0ng-password");
        var unknown = await LoginAsync("nobody", "Wr0ng-password");
        Assert.Equal(unknown.Status, wrong.Status);
        Assert.DoesNotContain("confirm", wrong.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("waiting", wrong.Body, StringComparison.OrdinalIgnoreCase);

        // The right password gets the approval explanation, and no token.
        var right = await LoginAsync("carol", Password);
        Assert.NotEqual(HttpStatusCode.Created, right.Status);
        Assert.Contains("waiting for an administrator", right.Body);
        Assert.DoesNotContain("confirm its email", right.Body);

        // Guesses are counted: after enough, the account is locked out even for the right password.
        for (var i = 0; i < 12; i++) await LoginAsync("carol", "Wr0ng-password" + i);
        var locked = await LoginAsync("carol", Password);
        Assert.DoesNotContain("waiting for an administrator", locked.Body);
        Assert.Contains("locked", locked.Body);
    }

    [Fact]
    public async Task AnApiKeyOfAPendingUserGetsNoOciToken()
    {
        var (app, _) = await StartAsync();
        await CreateUserAsync(app, "frank");
        string key;
        using (var scope = app.Services.CreateScope())
        {
            var user = (await FindAsync(app, "frank"))!;
            key = await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
                .CreateAsync(user.Id, "k", ApiKeyScopes.All, "*", null, TestContext.Current.CancellationToken);
        }

        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        async Task<HttpStatusCode> TokenAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v2/token?service=BaGet");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("frank:" + key)));
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await TokenAsync());

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var user = (await users.FindByNameAsync("frank"))!;
            user.PendingApproval = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await TokenAsync());
    }

    // ---- 5: approve racing a delete ----

    private sealed class RacingUserManager : UserManager<BaGetUser>
    {
        private readonly Func<Task> _race;

        public RacingUserManager(
            IUserStore<BaGetUser> store,
            Microsoft.Extensions.Options.IOptions<IdentityOptions> options,
            IPasswordHasher<BaGetUser> hasher,
            IEnumerable<IUserValidator<BaGetUser>> userValidators,
            IEnumerable<IPasswordValidator<BaGetUser>> passwordValidators,
            ILookupNormalizer normalizer,
            IdentityErrorDescriber errors,
            IServiceProvider services,
            ILogger<UserManager<BaGetUser>> logger,
            Func<Task> race)
            : base(store, options, hasher, userValidators, passwordValidators, normalizer, errors, services, logger)
        {
            _race = race;
        }

        public override async Task<BaGetUser?> FindByNameAsync(string userName)
        {
            var user = await base.FindByNameAsync(userName);
            await _race(); // the account is deleted by someone else after it was read

            return user;
        }
    }

    [Fact]
    public async Task ApprovingAnAccountDeletedMeanwhileIsAConflictNotAnException()
    {
        var (app, _) = await StartAsync();
        await CreatePendingAsync(app, "carol");

        using var scope = app.Services.CreateScope();
        var racing = ActivatorUtilities.CreateInstance<RacingUserManager>(scope.ServiceProvider, (Func<Task>)(async () =>
        {
            using var other = app.Services.CreateScope();
            var users = other.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var user = await users.FindByNameAsync("carol");
            if (user != null) Assert.True((await users.DeleteAsync(user)).Succeeded);
        }));
        var service = new AccountApprovalService(racing, scope.ServiceProvider.GetRequiredService<IContext>(), scope.ServiceProvider.GetRequiredService<ILogger<AccountApprovalService>>());

        Assert.Equal(AccountApprovalResult.Conflict, await service.ApproveAsync("carol", "test", TestContext.Current.CancellationToken));
        Assert.Null(await FindAsync(app, "carol"));
    }

    // ---- 6: detail page and REST ----

    [Fact]
    public async Task TheUserDetailPageShowsThePendingStateAndApprovesWithThePassword()
    {
        var (app, admin) = await StartAsync();
        await CreatePendingAsync(app, "carol");

        var page = await admin.GetPageAsync("admin/users/carol");
        Assert.Contains("Awaiting approval", page);
        Assert.Contains("id=\"approve-form\"", page);

        using var wrong = await admin.PostAsync("admin/users/carol", new Dictionary<string, string> { { "password", "nope" } }, action: "admin/users/carol?handler=Approve");
        Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        Assert.True((await FindAsync(app, "carol"))!.PendingApproval);

        using var approved = await admin.PostAsync("admin/users/carol", new Dictionary<string, string> { { "password", Password } }, action: "admin/users/carol?handler=Approve");
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Assert.False((await FindAsync(app, "carol"))!.PendingApproval);
        var after = await admin.GetPageAsync("admin/users/carol");
        Assert.DoesNotContain("id=\"approve-form\"", after);
        Assert.Contains("carol was approved", after);
    }

    [Fact]
    public async Task ApprovingIsRefusedWhileTheReadOnlyGateIsClosed()
    {
        var (app, admin) = await StartAsync();
        await CreatePendingAsync(app, "carol");
        var users = await admin.GetPageAsync("admin/users"); // the antiforgery token is taken while the gate is open
        Assert.Contains("Awaiting approval", users);
        app.Services.GetRequiredService<MaintenanceGate>().CloseUntilRestart("test");

        using var list = await admin.PostAsync("admin/users", new Dictionary<string, string> { { "userName", "carol" }, { "password", Password } },
            action: "admin/users?handler=Approve");
        using var detail = await admin.PostAsync("admin/users/carol", new Dictionary<string, string> { { "password", Password } },
            action: "admin/users/carol?handler=Approve");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, list.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, detail.StatusCode);
        Assert.True((await FindAsync(app, "carol"))!.PendingApproval);
    }

    [Fact]
    public async Task TheRestApiReportsThePendingState()
    {
        using var harness = new AdminApiHarness(_output, new Dictionary<string, string> { { "Accounts:RequireApproval", "true" } });
        await CreatePendingAsync(harness.App, "carol");

        var one = await harness.GetAsync("/api/v1/users/carol", harness.RootKey);
        Assert.Equal(HttpStatusCode.OK, one.Status);
        Assert.True(one.Json["user"]!["pendingApproval"]!.GetValue<bool>());

        var list = await harness.GetAsync("/api/v1/users?q=carol", harness.RootKey);
        Assert.True(list.Json["items"]![0]!["pendingApproval"]!.GetValue<bool>());

        var alice = await harness.GetAsync("/api/v1/users/alice", harness.RootKey);
        Assert.False(alice.Json["user"]!["pendingApproval"]!.GetValue<bool>());
    }

    // ---- 7: the external provider ----

    private async Task<(BaGetApplication App, Browser Browser)> StartWithProviderAsync()
    {
        var (app, _) = await StartAsync(
            new Dictionary<string, string>
            {
                { "Accounts:OpenIdConnect:Authority", "https://idp.example.com" },
                { "Accounts:OpenIdConnect:ClientId", "baget" },
                { "Accounts:OpenIdConnect:ClientSecret", "secret" },
            },
            services =>
            {
                services.AddTransient<IStartupFilter, ExternalCookieFilter>();
                services.PostConfigure<OpenIdConnectOptions>(
                    AccountsServiceCollectionExtensions.OpenIdConnectScheme,
                    options => options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration
                        {
                            Issuer = "https://idp.example.com",
                            AuthorizationEndpoint = "https://idp.example.com/authorize",
                        }));
            });
        var browser = new Browser(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    [Fact]
    public async Task TheFirstExternalSignInCreatesAPendingAccountAndAnExistingOneIsRefused()
    {
        var (app, browser) = await StartWithProviderAsync();

        using (await browser.GetAsync("test/external?sub=sub-1&name=gina&email=gina@example.com&verified=true")) { }
        using (var first = await browser.GetAsync("account/login?handler=Callback"))
        {
            // Created, not signed in.
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Contains("An administrator must enable it", await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.True((await FindAsync(app, "gina"))!.PendingApproval);

        using (await browser.GetAsync("test/external?sub=sub-1&name=gina&email=gina@example.com&verified=true")) { }
        using (var second = await browser.GetAsync("account/login?handler=Callback"))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Contains("waiting for an administrator", await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // No second account was made for the same provider login, and the session is not signed in.
        using (var scope = app.Services.CreateScope())
        {
            Assert.Equal(1, scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().Users.Count(u => u.UserName!.StartsWith("gina")));
        }

        using var profile = await browser.GetAsync("account/settings");
        Assert.NotEqual(HttpStatusCode.OK, profile.StatusCode);
    }
}
