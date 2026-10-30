using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Gravatar avatars on package owners, profiles and the header menu, and the switch that
/// turns every request to gravatar.com off.
/// </summary>
public class AvatarTests : IDisposable
{
    private const string Password = "Passw0rd!";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public AvatarTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private (BaGetApplication App, Browser Browser) Start(bool useGravatar = true)
    {
        var settings = new Dictionary<string, string>
        {
            { "Accounts:Enabled", "true" },
            { "ApiKey", "" },
        };
        if (!useGravatar) settings["Site:UseGravatar"] = "false";

        var app = new BaGetApplication(_output, configOverrides: settings);
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    private static string Md5(string value)
    {
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static async Task CreateUserAsync(BaGetApplication app, string name, string? email)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var result = await users.CreateAsync(
            new BaGetUser { UserName = name, Email = email, EmailConfirmed = email != null, Created = DateTime.UtcNow },
            Password);
        Assert.True(result.Succeeded);
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

    private static async Task AddOrganizationAsync(BaGetApplication app, string name)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        #pragma warning disable CS8602
        context.Organizations.Add(new Organization { Name = name, Created = DateTime.UtcNow });
        #pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task SignInAsync(Browser browser, string name)
    {
        using var response = await browser.PostAsync("account/login", new Dictionary<string, string>
        {
            { "UserName", name },
            { "Password", Password },
        });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task AnOwnerWithAnEmailGetsAGravatarOfTheTrimmedLowercasedAddress()
    {
        var (app, browser) = Start();
        await CreateUserAsync(app, "alice", "Alice@Example.COM");
        await AddPackageAsync(app, "Avatar.Pkg", "alice");

        var html = await browser.GetPageAsync("packages/Avatar.Pkg");

        Assert.Contains($"https://www.gravatar.com/avatar/{Md5("alice@example.com")}?s=", html);
        Assert.Contains("d=retro", html);
        Assert.DoesNotContain("alice@example.com", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnOrganizationHashesItsName()
    {
        var (app, browser) = Start();
        await AddOrganizationAsync(app, "Acme");
        await AddPackageAsync(app, "Avatar.Pkg", "Acme");

        var html = await browser.GetPageAsync("packages/Avatar.Pkg");

        Assert.Contains($"gravatar.com/avatar/{Md5("acme")}?s=", html);
    }

    [Fact]
    public async Task AUserWithoutAnEmailHashesTheirName()
    {
        var (app, browser) = Start();
        await CreateUserAsync(app, "Noemail", null);
        await AddPackageAsync(app, "Avatar.Pkg", "Noemail");

        var html = await browser.GetPageAsync("packages/Avatar.Pkg");

        Assert.Contains($"gravatar.com/avatar/{Md5("noemail")}?s=", html);
    }

    [Fact]
    public async Task TurningGravatarOffMakesNoRequestToItAnywhereAndFallsBackToTheInitial()
    {
        var (app, browser) = Start(useGravatar: false);
        await CreateUserAsync(app, "alice", "alice@example.com");
        await AddPackageAsync(app, "Avatar.Pkg", "alice");

        var package = await browser.GetPageAsync("packages/Avatar.Pkg");
        var profile = await browser.GetPageAsync("profiles/alice");
        await SignInAsync(browser, "alice");
        var signedIn = await browser.GetPageAsync("packages/Avatar.Pkg");

        Assert.DoesNotContain("gravatar.com", package, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gravatar.com", profile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gravatar.com", signedIn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"owner-avatar\"", package);
        Assert.Contains(">A</span>", package);
    }

    [Fact]
    public async Task TheProfileShowsTheAvatar()
    {
        var (app, browser) = Start();
        await CreateUserAsync(app, "alice", "alice@example.com");

        var html = await browser.GetPageAsync("profiles/alice");

        Assert.Contains($"gravatar.com/avatar/{Md5("alice@example.com")}?s=", html);
    }

    [Fact]
    public async Task TheHeaderMenuShowsTheSignedInUsersAvatar()
    {
        var (app, browser) = Start();
        await CreateUserAsync(app, "alice", "alice@example.com");
        await SignInAsync(browser, "alice");

        var html = await browser.GetPageAsync("");

        Assert.Contains($"gravatar.com/avatar/{Md5("alice@example.com")}?s=", html);
    }

    [Fact]
    public async Task SignedOutHeaderHasNoAvatar()
    {
        var (_, browser) = Start();

        var html = await browser.GetPageAsync("");

        Assert.DoesNotContain("gravatar.com", html, StringComparison.OrdinalIgnoreCase);
    }
}
