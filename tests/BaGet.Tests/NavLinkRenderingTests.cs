using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The layout's <c>nav-link</c> attribute is handled by <c>BaGet.Web.NavLinkTagHelper</c>, which lives in the
/// <c>BaGet.Web.V3</c> assembly: <c>_ViewImports</c> must name that assembly in <c>@addTagHelper</c>, else the tag
/// helper silently stops applying and no link is ever marked active.
/// </summary>
public class NavLinkRenderingTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly Browser _browser;

    public NavLinkRenderingTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output);
        _browser = new Browser(_app);
    }

    public void Dispose()
    {
        _browser.Dispose();
        _app.Dispose();
    }

    private static string ClassOfLinkTo(string html, string hrefEnd)
    {
        var match = Regex.Match(html, "<a [^>]*href=\"[^\"]*" + Regex.Escape(hrefEnd) + "\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"no link to {hrefEnd} in the header");

        var cls = Regex.Match(match.Value, "class=\"([^\"]*)\"");
        return cls.Success ? cls.Groups[1].Value : string.Empty;
    }

    [Fact]
    public async Task TheLinkOfTheCurrentPageIsMarkedActive()
    {
        var html = await _browser.GetPageAsync("/stats");

        Assert.Contains("active", ClassOfLinkTo(html, "/stats").Split(' '));
        Assert.DoesNotContain("active", ClassOfLinkTo(html, "/packages").Split(' '));
    }

    [Fact]
    public async Task ThePackagesLinkIsActiveOnTheFrontPage()
    {
        var html = await _browser.GetPageAsync("/");

        Assert.Contains("active", ClassOfLinkTo(html, "/packages").Split(' '));
    }
}
