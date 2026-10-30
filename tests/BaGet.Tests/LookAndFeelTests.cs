using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The nuget.org-style front page, search result cards, header search bar and footer.
/// </summary>
public class LookAndFeelTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BaGetLook", Guid.NewGuid().ToString("N"));

    public LookAndFeelTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    private (BaGetApplication App, Browser Browser) Start(Dictionary<string, string>? settings = null)
    {
        var app = new BaGetApplication(_output, configOverrides: settings);
        var browser = new Browser(app);
        _disposables.Add(app);
        _disposables.Add(browser);

        return (app, browser);
    }

    private static async Task AddTestPackageAsync(BaGetApplication app)
    {
        using var package = TestResources.GetResourceStream(TestResources.Package);
        await app.AddPackageAsync(package);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public async Task HomeShowsTheFeedTotals()
    {
        var (app, browser) = Start();
        await AddTestPackageAsync(app);

        var html = await browser.GetPageAsync("");

        Assert.Contains("id=\"home-packages\">1<", html);
        Assert.Contains("id=\"home-versions\">1<", html);
        Assert.Contains("id=\"home-downloads\">0<", html);
        Assert.Contains("home-hero", html);
        Assert.DoesNotContain("package-card", html);
    }

    [Fact]
    public async Task HomeHasTheHeroSearchButNoHeaderSearchBar()
    {
        var (_, browser) = Start();

        var home = await browser.GetPageAsync("");
        var packages = await browser.GetPageAsync("packages");

        Assert.DoesNotContain("search-band", home);
        Assert.Contains("name=\"q\"", home);
        Assert.Contains("search-band", packages);
        Assert.Contains("search-band", await browser.GetPageAsync("stats"));
    }

    [Fact]
    public async Task ThePackagesPageListsPackagesWithoutAQuery()
    {
        var (app, browser) = Start();
        await AddTestPackageAsync(app);

        var html = await browser.GetPageAsync("packages");

        Assert.Contains("package-card", html);
        Assert.DoesNotContain("home-hero", html);
    }

    [Fact]
    public async Task AFilterWithoutAQueryShowsResultsNotTheFrontPage()
    {
        var (app, browser) = Start();
        await AddTestPackageAsync(app);

        var html = await browser.GetPageAsync("?prerelease=False");

        Assert.Contains("package-card", html);
        Assert.DoesNotContain("home-hero", html);
    }

    [Fact]
    public async Task CardsShowTheirFields()
    {
        var (app, browser) = Start();
        await AddTestPackageAsync(app);

        var html = await browser.GetPageAsync("?q=TestData");

        Assert.Contains("package-card", html);
        Assert.Contains("TestData", html);
        Assert.Contains("by Test author", html);
        Assert.Contains("0 total downloads", html);
        Assert.Contains("Last updated", html);
        Assert.Contains("Latest version: 1.2.3", html);
        Assert.Contains("Test description", html);
    }

    [Fact]
    public async Task CardsNameTheOwnersWhenAccountsAreOn()
    {
        var (app, browser) = Start(new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        await AddTestPackageAsync(app);
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            #pragma warning disable CS8602
            context.PackageOwners.Add(new PackageOwner { PackageId = "TestData", OwnerName = "alice" });
            #pragma warning restore CS8602
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var html = await browser.GetPageAsync("?q=TestData");

        Assert.Contains("href=\"/profiles/alice\"", html);
        Assert.DoesNotContain("by Test author", html);
    }

    [Fact]
    public async Task ADeprecatedPackageCarriesABadge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (app, browser) = Start(new Dictionary<string, string> { { "ApiKey", "secret" } });
        await AddTestPackageAsync(app);
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "v3/package/TestData/1.2.3/deprecation")
        {
            Content = new StringContent(@"{""reasons"":[""Legacy""],""message"":""Use Other""}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-NuGet-ApiKey", "secret");
        using (var put = await client.SendAsync(request, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var html = await browser.GetPageAsync("?q=TestData");

        Assert.Contains("badge-deprecated", html);
    }

    [Fact]
    public async Task FiltersStillApply()
    {
        var (app, browser) = Start();
        await AddTestPackageAsync(app);

        Assert.Contains("package-card", await browser.GetPageAsync("?q=TestData&framework=net5.0"));
        Assert.DoesNotContain("package-card", await browser.GetPageAsync("?q=TestData&packageType=dotnettool"));
        Assert.DoesNotContain("package-card", await browser.GetPageAsync("?q=NoSuchThing"));
    }

    [Fact]
    public async Task TheFooterNamesTheProductAndHidesUnsetLegalLinks()
    {
        var (_, browser) = Start();

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("BaGet Extension", html);
        Assert.DoesNotContain("Powered by", html);
        Assert.Contains("Documentation", html);
        Assert.Contains("GitHub", html);
        Assert.DoesNotContain("footer-terms", html);
        Assert.DoesNotContain("footer-privacy", html);
    }

    [Fact]
    public async Task TheFooterLinksConfiguredLegalPages()
    {
        var (_, browser) = Start(new Dictionary<string, string>
        {
            { "Site:TermsOfUsePath", WriteFile("terms.md", "# Our terms") },
            { "Site:PrivacyPolicyPath", WriteFile("privacy.md", "# Our privacy") },
        });

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("href=\"/terms\"", html);
        Assert.Contains("href=\"/privacy\"", html);
    }

    [Fact]
    public async Task OnlyTheConfiguredLegalLinkIsShown()
    {
        var (_, browser) = Start(new Dictionary<string, string>
        {
            { "Site:TermsOfUsePath", WriteFile("terms.md", "x") },
        });

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("footer-terms", html);
        Assert.DoesNotContain("footer-privacy", html);
    }

    [Fact]
    public async Task TheTermsPageRendersTheConfiguredMarkdown()
    {
        var (_, browser) = Start(new Dictionary<string, string>
        {
            { "Site:TermsOfUsePath", WriteFile("terms.md", "## Our terms\n\nBe **kind**.") },
        });

        var html = await browser.GetPageAsync("terms");

        Assert.Contains(">Our terms</h2>", html);
        Assert.Contains("<strong>kind</strong>", html);
    }

    [Fact]
    public async Task TheLegalPagesDoNotInjectRawHtml()
    {
        var (_, browser) = Start(new Dictionary<string, string>
        {
            { "Site:PrivacyPolicyPath", WriteFile("privacy.md", "Hello <script>alert(1)</script>\n\n<img src=x onerror=alert(2)>") },
        });

        var html = await browser.GetPageAsync("privacy");

        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("terms")]
    [InlineData("privacy")]
    public async Task LegalPagesAre404WhenUnset(string page)
    {
        var (_, browser) = Start();

        using var response = await browser.GetAsync(page);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void ALegalPagePathToANonMarkdownFileIsRefusedAtStartup()
    {
        // The host refuses to start, as for any other invalid options.
        Assert.ThrowsAny<Exception>(() => Start(new Dictionary<string, string>
        {
            { "Site:TermsOfUsePath", WriteFile("secrets.json", "{}") },
        }));
    }

    [Fact]
    public async Task ALegalPageIs404WhenItsFileIsMissing()
    {
        var (_, browser) = Start(new Dictionary<string, string>
        {
            { "Site:TermsOfUsePath", Path.Combine(_directory, "missing.md") },
        });

        using var response = await browser.GetAsync("terms");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheHeaderCarriesTheBrandAndTheNavigation()
    {
        // Upload is listed only where an upload can work: here, by accounts being on.
        var (_, browser) = Start(new Dictionary<string, string> { { "Accounts:Enabled", "true" } });

        var html = await browser.GetPageAsync("stats");

        Assert.Contains("class=\"navbar-brand brand\"", html);
        Assert.Contains("Packages", html);
        Assert.Contains("Upload", html);
        Assert.Contains("Statistics", html);
        Assert.DoesNotContain("nuget.org", html, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables.AsEnumerable().Reverse())
        {
            disposable.Dispose();
        }
    }
}
