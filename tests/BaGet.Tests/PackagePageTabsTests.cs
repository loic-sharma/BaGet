using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The nuget.org-style package page: tabs, Frameworks, install snippets and the About sidebar.
/// </summary>
public class PackagePageTabsTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public PackagePageTabsTests(ITestOutputHelper output) => _output = output;

    private (BaGetApplication App, Browser Browser) Start(Dictionary<string, string>? settings = null)
    {
        var app = new BaGetApplication(_output, configOverrides: settings);
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    private static async Task AddAsync(
        BaGetApplication app,
        string id,
        string version,
        string[] frameworks,
        Action<Package>? customize = null)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var parsed = NuGetVersion.Parse(version);

        var package = new Package
        {
            Id = id,
            Version = parsed,
            Authors = new[] { "An Author" },
            Description = "Description",
            Listed = true,
            IsPrerelease = parsed.IsPrerelease,
            SemVerLevel = SemVerLevel.Unknown,
            Published = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Summary = "Summary",
            Tags = Array.Empty<string>(),
            Dependencies = new List<PackageDependency>(),
            PackageTypes = new List<PackageType>(),
            TargetFrameworks = new List<TargetFramework>(),
        };
        foreach (var f in frameworks) package.TargetFrameworks.Add(new TargetFramework { Moniker = f });
        customize?.Invoke(package);

        #pragma warning disable CS8602
        context.Packages.Add(package);
        #pragma warning restore CS8602
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static string Text(string html) => WebUtility.HtmlDecode(html);

    /// <summary>
    /// The site ships Alpine 2, where "$el" is the component root and "init()" is not called. The
    /// behaviour runs in the browser, so this asserts the markup uses neither.
    /// </summary>
    [Fact]
    public async Task TheMarkupUsesOnlyAlpine2Constructs()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Alpine.Pkg", "1.0.0", new[] { "net8.0" });

        var html = Text(await browser.GetPageAsync("packages/Alpine.Pkg"));

        Assert.DoesNotContain("$el.closest", html);
        Assert.Contains("$event.currentTarget.closest('.install-pane')", html);
        Assert.DoesNotContain("init()", html);
        Assert.Contains("x-init=\"selectFromHash()\"", html);
    }

    [Fact]
    public async Task TheSixTabsAppearInNugetOrgOrder()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Tabbed.Pkg", "1.0.0", new[] { "net8.0" });

        var html = await browser.GetPageAsync("packages/Tabbed.Pkg");

        var order = new[] { "tab-readme", "tab-frameworks", "tab-dependencies", "tab-usedby", "tab-versions", "tab-releasenotes" };
        var last = -1;
        foreach (var tab in order)
        {
            var index = html.IndexOf($"href=\"#{tab}\"", StringComparison.Ordinal);
            Assert.True(index > last, $"{tab} missing or out of order");
            last = index;
            Assert.Contains($"id=\"{tab}\"", html);
        }
    }

    [Fact]
    public async Task EveryPanelIsServerRenderedSoNoJavascriptStillShowsTheContent()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Tabbed.Pkg", "1.0.0", new[] { "net8.0" }, p => p.ReleaseNotes = "Fixed the thing");

        var html = await browser.GetPageAsync("packages/Tabbed.Pkg");

        Assert.Contains("Fixed the thing", html);
        Assert.Contains("This package has no dependencies.", html);
        Assert.Contains("No packages depend on Tabbed.Pkg.", html);
    }

    [Fact]
    public async Task ReleaseNotesTabSaysSoWhenThereAreNone()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Tabbed.Pkg", "1.0.0", new[] { "net8.0" });

        var html = await browser.GetPageAsync("packages/Tabbed.Pkg");

        Assert.Contains("This package has no release notes.", html);
    }

    [Fact]
    public async Task FrameworksTabListsDeclaredAndComputed()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Tabbed.Pkg", "1.0.0", new[] { "netstandard2.0", "net48" });

        var html = await browser.GetPageAsync("packages/Tabbed.Pkg");

        Assert.Contains("framework-group", html);
        Assert.Contains("data-framework=\"netstandard2.0\" data-kind=\"declared\"", html);
        Assert.Contains("data-framework=\"net48\" data-kind=\"declared\"", html);
        Assert.Contains("data-framework=\"net10.0\" data-kind=\"computed\"", html);
        Assert.Contains("data-framework=\"netstandard2.1\" data-kind=\"computed\"", html);
        Assert.DoesNotContain("data-framework=\"netstandard2.0\" data-kind=\"computed\"", html);
    }

    [Fact]
    public async Task FrameworksTabHandlesAPackageWithNoDeclaredFrameworks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, browser) = Start();
        await AddAsync(app, "Content.Only", "1.0.0", Array.Empty<string>());

        using var response = await browser.GetAsync("packages/Content.Only");
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("This package does not target a specific framework.", html);
        Assert.DoesNotContain("framework-group", html);
    }

    [Theory]
    [InlineData("#r \"nuget: Snip.Pkg, 1.2.3\"")]
    [InlineData("#:package Snip.Pkg@1.2.3")]
    [InlineData("#addin nuget:?package=Snip.Pkg&version=1.2.3")]
    [InlineData("#tool nuget:?package=Snip.Pkg&version=1.2.3")]
    [InlineData("nuke :add-package Snip.Pkg --version 1.2.3")]
    [InlineData("<PackageVersion Include=\"Snip.Pkg\" Version=\"1.2.3\" />")]
    [InlineData("<PackageReference Include=\"Snip.Pkg\" />")]
    [InlineData("<PackageReference Include=\"Snip.Pkg\" Version=\"1.2.3\" />")]
    [InlineData("dotnet add package Snip.Pkg --version 1.2.3")]
    [InlineData("paket add Snip.Pkg --version 1.2.3")]
    [InlineData("Install-Package Snip.Pkg -Version 1.2.3")]
    public async Task InstallSnippetTextIsRenderedExactly(string expected)
    {
        var (app, browser) = Start();
        await AddAsync(app, "Snip.Pkg", "1.2.3", new[] { "net8.0" });

        var html = Text(await browser.GetPageAsync("packages/Snip.Pkg"));

        Assert.Contains(expected, html);
    }

    [Fact]
    public async Task PrereleaseSnippetsCarryTheVersionVerbatim()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Snip.Pkg", "2.0.0-beta.1", new[] { "net8.0" });

        var html = Text(await browser.GetPageAsync("packages/Snip.Pkg"));

        Assert.Contains("#r \"nuget: Snip.Pkg, 2.0.0-beta.1\"", html);
        Assert.Contains("#:package Snip.Pkg@2.0.0-beta.1", html);
        Assert.Contains("#addin nuget:?package=Snip.Pkg&version=2.0.0-beta.1&prerelease", html);
        Assert.Contains("nuke :add-package Snip.Pkg --version 2.0.0-beta.1", html);
    }

    [Fact]
    public async Task AboutSidebarShowsMetadataLinks()
    {
        var (app, browser) = Start();
        await AddAsync(app, "About.Pkg", "1.0.0", new[] { "net8.0" }, p =>
        {
            p.ProjectUrl = new Uri("https://example.org/project");
            p.RepositoryUrl = new Uri("https://example.org/repo.git");
            p.LicenseUrl = new Uri("https://licenses.nuget.org/MIT");
        });

        var html = await browser.GetPageAsync("packages/About.Pkg");

        Assert.Contains("<h2>About</h2>", html);
        Assert.Contains("href=\"https://example.org/project\"", html);
        Assert.Contains("href=\"https://example.org/repo.git\"", html);
        Assert.Contains("href=\"https://licenses.nuget.org/MIT\"", html);
        Assert.Contains("Project website", html);
        Assert.Contains("Source repository", html);
        Assert.Contains("Download package", html);
        Assert.Contains("Report package", html);
        Assert.Contains("Last updated", html);
    }

    [Fact]
    public async Task AboutSidebarOmitsLinksTheMetadataLacks()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Bare.Pkg", "1.0.0", new[] { "net8.0" });

        var html = await browser.GetPageAsync("packages/Bare.Pkg");

        Assert.DoesNotContain("Project website", html);
        Assert.DoesNotContain("Source repository", html);
        Assert.DoesNotContain("href=\"\"", html);
        Assert.DoesNotContain("Download symbols", html);
    }

    /// <summary>
    /// As on nuget.org: the shown version on nuget.info, opened in a new tab as a third-party link,
    /// and a desktop variant that downloads the .nupkg for NuGet Package Explorer's file association.
    /// </summary>
    [Fact]
    public async Task AboutSidebarLinksToNuGetPackageExplorer()
    {
        var (app, browser) = Start();
        await AddAsync(app, "Explore.Pkg", "1.0.0", new[] { "net8.0" });
        await AddAsync(app, "Explore.Pkg", "2.0.0-beta.1", new[] { "net8.0" });

        var html = Text(await browser.GetPageAsync("packages/Explore.Pkg/2.0.0-beta.1"));

        Assert.Contains("href=\"https://nuget.info/packages/Explore.Pkg/2.0.0-beta.1\" target=\"_blank\" rel=\"nofollow noreferrer\"", html);
        Assert.Contains("nuget.info is a 3rd party website", html);
        Assert.Contains("Open in NuGet Package Explorer (desktop)", html);
        Assert.Matches("<a href=\"[^\"]*/explore\\.pkg/2\\.0\\.0-beta\\.1/explore\\.pkg\\.2\\.0\\.0-beta\\.1\\.nupkg\" download", html);
    }

    [Fact]
    public async Task OwnersAppearWithAnAvatarWhenAccountsAreOn()
    {
        var (app, browser) = Start(new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        await AddAsync(app, "Owned.Pkg", "1.0.0", new[] { "net8.0" });
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            #pragma warning disable CS8602
            context.PackageOwners.Add(new PackageOwner { PackageId = "Owned.Pkg", OwnerName = "alice" });
            #pragma warning restore CS8602
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var html = await browser.GetPageAsync("packages/Owned.Pkg");

        Assert.Contains("owner-avatar", html);
        Assert.Contains(">alice<", html);
        Assert.Contains("gravatar.com/avatar/", html);
    }

    [Fact]
    public async Task AccountsDisabledStillRendersWithoutOwners()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, browser) = Start();
        await AddAsync(app, "Plain.Pkg", "1.0.0", new[] { "net8.0" });

        using var response = await browser.GetAsync("packages/Plain.Pkg");
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("owner-avatar", html);
        Assert.DoesNotContain("Manage package", html);
    }

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }
}
