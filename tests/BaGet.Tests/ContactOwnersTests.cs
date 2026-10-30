using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Contact owners: who receives the message, what is hidden from the sender, and the
/// limits that keep the page from being a spam relay.
/// </summary>
public class ContactOwnersTests : IDisposable
{
    private const string Password = "Passw0rd!";
    private const string Url = "packages/Contact.Pkg/contact-owners";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();
    private readonly MutableTime _time = new();

    public ContactOwnersTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private sealed class MutableTime : SystemTime
    {
        public DateTime Now { get; set; } = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public override DateTime UtcNow => Now;
    }

    private (BaGetApplication App, Browser Browser) Start(bool email = true, bool accounts = true, Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string>
        {
            { "Accounts:Enabled", accounts ? "true" : "false" },
            { "ApiKey", "" },
        };
        if (email)
        {
            settings["Email:Smtp:Host"] = "smtp.example.com";
            settings["Email:Smtp:From"] = "baget@example.com";
            settings["Site:PublicUrl"] = "https://baget.example.com";
        }

        var app = new BaGetApplication(
            _output,
            configOverrides: settings,
            configureServices: services =>
            {
                services.AddSingleton<SystemTime>(_time);
                configure?.Invoke(services);
            });
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    private static async Task CreateUserAsync(BaGetApplication app, string name, string? email, bool confirmed = true)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var result = await users.CreateAsync(
            new BaGetUser { UserName = name, Email = email, EmailConfirmed = confirmed, Created = DateTime.UtcNow },
            Password);
        Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(e => e.Description)));
    }

    private static async Task AddPackageAsync(BaGetApplication app, string id, params string[] owners)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        #pragma warning disable CS8602
        context.Packages.Add(new Package
        {
            Id = id,
            Version = NuGetVersion.Parse("1.0.0"),
            Authors = new[] { "An Author" },
            Description = "Description",
            Listed = true,
            SemVerLevel = SemVerLevel.Unknown,
            Published = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Summary = "Summary",
            Tags = Array.Empty<string>(),
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        });
        foreach (var owner in owners) context.PackageOwners.Add(new PackageOwner { PackageId = id, OwnerName = owner });
        #pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task AddOrganizationAsync(BaGetApplication app, string name, params (string User, bool Admin)[] members)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var organization = new Organization { Name = name, Created = DateTime.UtcNow };
        #pragma warning disable CS8602
        context.Organizations.Add(organization);
        #pragma warning restore CS8602
        foreach (var (user, admin) in members)
        {
            var account = await users.FindByNameAsync(user);
            #pragma warning disable CS8602
            context.OrganizationMembers.Add(new OrganizationMember { Organization = organization, UserId = account.Id, IsAdmin = admin });
            #pragma warning restore CS8602
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task SignInAsync(Browser browser, string name)
    {
        using var response = await browser.PostAsync("account/login", new Dictionary<string, string>
        {
            { "UserName", name },
            { "Password", Password },
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        Browser browser,
        string message,
        bool copy = false,
        string url = Url)
    {
        var fields = new Dictionary<string, string> { { "Message", message } };
        if (copy) fields["CopyToSelf"] = "true";

        using var response = await browser.PostAsync(url, fields);

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static IReadOnlyList<CapturedEmail> Contacts(BaGetApplication app)
        => app.Services.GetRequiredService<CapturingEmailSender>().Sent.Where(e => e.Kind == "contact").ToList();

    /// <summary>
    /// owner1 and acme (admin1 admin, member1 member) own Contact.Pkg; sender is signed in.
    /// </summary>
    private async Task<(BaGetApplication App, Browser Browser)> SeedAsync(Action<IServiceCollection>? configure = null)
    {
        var (app, browser) = Start(configure: configure);
        await CreateUserAsync(app, "owner1", "owner1@example.com");
        await CreateUserAsync(app, "admin1", "admin1@example.com");
        await CreateUserAsync(app, "member1", "member1@example.com");
        await CreateUserAsync(app, "sender", "sender@example.com");
        await AddOrganizationAsync(app, "acme", ("admin1", true), ("member1", false));
        await AddPackageAsync(app, "Contact.Pkg", "owner1", "acme");
        await AddPackageAsync(app, "Other.Pkg", "owner1");
        await SignInAsync(browser, "sender");

        return (app, browser);
    }

    // --- Gating -------------------------------------------------------------------------

    [Fact]
    public async Task WithoutEmailThePageIsNotFoundAndThePackagePageHasNoLink()
    {
        var (app, browser) = Start(email: false);
        await CreateUserAsync(app, "owner1", "owner1@example.com");
        await CreateUserAsync(app, "sender", "sender@example.com");
        await AddPackageAsync(app, "Contact.Pkg", "owner1");
        await SignInAsync(browser, "sender");

        using var page = await browser.GetAsync(Url);
        var html = await browser.GetPageAsync("packages/Contact.Pkg");

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.DoesNotContain("contact-owners", html);
    }

    [Fact]
    public async Task WithAccountsDisabledThePageIsNotFound()
    {
        var (app, browser) = Start(accounts: false);
        await AddPackageAsync(app, "Contact.Pkg");

        using var page = await browser.GetAsync(Url);

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
    }

    [Fact]
    public async Task AnonymousVisitorsAreAskedToSignInAndSeeNoLink()
    {
        var (app, browser) = Start();
        await CreateUserAsync(app, "owner1", "owner1@example.com");
        await AddPackageAsync(app, "Contact.Pkg", "owner1");

        using var page = await browser.GetAsync(Url);
        var html = await browser.GetPageAsync("packages/Contact.Pkg");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        #pragma warning disable CS8602
        Assert.Contains("account/login", page.Headers.Location.ToString());
        #pragma warning restore CS8602
        Assert.DoesNotContain("contact-owners", html);
    }

    [Fact]
    public async Task SignedInUsersSeeTheLinkInTheSidebar()
    {
        var (_, browser) = await SeedAsync();

        var html = await browser.GetPageAsync("packages/Contact.Pkg");

        Assert.Contains("packages/Contact.Pkg/contact-owners", html);
        Assert.Contains("Contact owners", html);
    }

    [Fact]
    public async Task APackageWithoutOwnersOrAnUnknownOneIsNotFound()
    {
        var (app, browser) = await SeedAsync();
        await AddPackageAsync(app, "Orphan.Pkg");

        using var orphan = await browser.GetAsync("packages/Orphan.Pkg/contact-owners");
        using var unknown = await browser.GetAsync("packages/Nope.Pkg/contact-owners");

        Assert.Equal(HttpStatusCode.NotFound, orphan.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task APostWithoutAnAntiforgeryTokenIsRefused()
    {
        var (app, browser) = await SeedAsync();

        using var response = await browser.RawPostAsync(Url, new Dictionary<string, string> { { "Message", "hi" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Contacts(app));
    }

    // --- Delivery -----------------------------------------------------------------------

    [Fact]
    public async Task ReachesEveryUserOwnerAndOwningOrganizationAdminsButNotPlainMembers()
    {
        var (app, browser) = await SeedAsync();

        var (status, _) = await SendAsync(browser, "Please add net10.0 support.");

        Assert.Equal(HttpStatusCode.OK, status);
        var sent = Contacts(app);
        Assert.Equal(
            new[] { "admin1@example.com", "owner1@example.com" },
            sent.Select(e => e.To).OrderBy(t => t, StringComparer.Ordinal));
        Assert.All(sent, e =>
        {
            Assert.Equal("sender@example.com", e.ReplyTo);
            Assert.Equal("Message about your package Contact.Pkg", e.Subject);
            Assert.Contains("Please add net10.0 support.", e.Payload);
        });
    }

    [Fact]
    public async Task NoOwnerAddressIsRevealedToTheSender()
    {
        var (_, browser) = await SeedAsync();

        var form = await browser.GetPageAsync(Url);
        var (_, body) = await SendAsync(browser, "Hello");

        foreach (var html in new[] { form, body })
        {
            Assert.DoesNotContain("owner1@example.com", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("admin1@example.com", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("member1@example.com", html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UsersWithoutAConfirmedEmailAreSkippedAndEachAddressIsMailedOnce()
    {
        var (app, browser) = Start();
        await CreateUserAsync(app, "owner1", "shared@example.com");
        await CreateUserAsync(app, "owner2", "SHARED@example.com");
        await CreateUserAsync(app, "owner3", "unconfirmed@example.com", confirmed: false);
        await CreateUserAsync(app, "sender", "sender@example.com");
        await AddPackageAsync(app, "Contact.Pkg", "owner1", "owner2", "owner3");
        await SignInAsync(browser, "sender");

        await SendAsync(browser, "Hello");

        var to = Contacts(app).Select(e => e.To.ToLowerInvariant()).ToList();
        Assert.Equal(new[] { "shared@example.com" }, to);
    }

    [Fact]
    public async Task CopyToSelfSendsTheSenderOneMoreCopy()
    {
        var (app, browser) = await SeedAsync();

        await SendAsync(browser, "Hello", copy: true);

        var sent = Contacts(app);
        Assert.Equal(3, sent.Count);
        Assert.Contains(sent, e => e.To == "sender@example.com");
    }

    [Fact]
    public async Task NoCopyIsSentUnlessAsked()
    {
        var (app, browser) = await SeedAsync();

        await SendAsync(browser, "Hello");

        Assert.DoesNotContain(Contacts(app), e => e.To == "sender@example.com");
    }

    // --- Validation ---------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyMessageSendsNothing(string message)
    {
        var (app, browser) = await SeedAsync();

        var (status, _) = await SendAsync(browser, message);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(Contacts(app));
    }

    [Fact]
    public async Task ATooLongMessageSendsNothingAndDoesNotUseUpTheAllowance()
    {
        var (app, browser) = await SeedAsync();

        await SendAsync(browser, new string('x', 5000));
        Assert.Empty(Contacts(app));

        await SendAsync(browser, "Short");
        Assert.NotEmpty(Contacts(app));
    }

    [Fact]
    public async Task HeaderInjectionInTheMessageStaysInsideTheBody()
    {
        var (app, browser) = await SeedAsync();

        await SendAsync(browser, "Hi\r\nBcc: victim@example.com\r\nSubject: pwned");

        var sent = Contacts(app);
        Assert.NotEmpty(sent);
        Assert.All(sent, e =>
        {
            Assert.Equal("Message about your package Contact.Pkg", e.Subject);
            Assert.DoesNotContain("victim@example.com", e.To);
            Assert.Equal("sender@example.com", e.ReplyTo);
        });
    }

    [Fact]
    public async Task ASenderWithoutAnEmailIsToldToAddOneAndNothingIsSent()
    {
        var (app, browser) = Start();
        await CreateUserAsync(app, "owner1", "owner1@example.com");
        await CreateUserAsync(app, "noemail", null);
        await AddPackageAsync(app, "Contact.Pkg", "owner1");
        await SignInAsync(browser, "noemail");

        var form = await browser.GetPageAsync(Url);
        await SendAsync(browser, "Hello");

        Assert.Contains("email address", form, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Contacts(app));
    }

    [Fact]
    public async Task ASenderWhoseEmailIsNotConfirmedCannotUseItAsReplyToOrForACopy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Identity re-checks a session only every so often by default, so a session can
        // outlive its user's confirmation; the page must not rely on the cookie alone.
        var (app, browser) = await SeedAsync(services => services.Configure<SecurityStampValidatorOptions>(
            validation => validation.ValidationInterval = TimeSpan.FromMinutes(30)));
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var sender = await users.FindByNameAsync("sender");
            #pragma warning disable CS8602
            sender.EmailConfirmed = false;
            #pragma warning restore CS8602
            Assert.True((await users.UpdateAsync(sender)).Succeeded);
        }

        // The form is not rendered for such a sender, so the token comes from another page.
        using var response = await browser.PostAsync(
            Url,
            new Dictionary<string, string> { { "Message", "Hello" }, { "CopyToSelf", "true" } },
            tokenPage: "account/settings");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Empty(Contacts(app));
        Assert.Contains("confirm", body, StringComparison.OrdinalIgnoreCase);
    }

    // --- Rate limit ---------------------------------------------------------------------

    [Fact]
    public async Task OneMessagePerSenderPerPackageInTenMinutes()
    {
        var (app, browser) = await SeedAsync();

        await SendAsync(browser, "First");
        var countAfterFirst = Contacts(app).Count;
        var (_, second) = await SendAsync(browser, "Second");

        Assert.Equal(2, countAfterFirst);
        Assert.Equal(countAfterFirst, Contacts(app).Count);
        Assert.Contains("10 minutes", second);
    }

    [Fact]
    public async Task TheAllowanceReturnsAfterTenMinutes()
    {
        var (app, browser) = await SeedAsync();

        await SendAsync(browser, "First");
        _time.Now = _time.Now.AddMinutes(9);
        await SendAsync(browser, "Too early");
        Assert.Equal(2, Contacts(app).Count);

        _time.Now = _time.Now.AddMinutes(2);
        await SendAsync(browser, "Later");
        Assert.Equal(4, Contacts(app).Count);
    }

    [Fact]
    public async Task TheLimitIsPerPackageAndPerSender()
    {
        var (app, browser) = await SeedAsync();
        await CreateUserAsync(app, "sender2", "sender2@example.com");

        await SendAsync(browser, "First");
        await SendAsync(browser, "Other package", url: "packages/Other.Pkg/contact-owners");
        Assert.Equal(3, Contacts(app).Count);

        using var second = new Browser(app);
        await SignInAsync(second, "sender2");
        await SendAsync(second, "Another sender");
        Assert.Equal(5, Contacts(app).Count);
    }
}
