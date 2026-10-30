using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// The Settings page shows one section at a time: the failed one, else <c>?section=</c>, else the first (General).
/// Every section stays in the markup (hidden) so the nav can switch client-side.
/// </summary>
public class SettingsSectionPageTests : IDisposable
{
    private const string Password = "Passw0rd!";

    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public SettingsSectionPageTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private async Task<Browser> StartAsync()
    {
        var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" } });
        _disposables.Add(app);
        var admin = new Browser(app);
        _disposables.Add(admin);

        await admin.RegisterAsync("root");
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdministratorService>()
                .PromoteAsync("root", "test", System.Threading.CancellationToken.None);
        }

        return admin;
    }

    internal static List<(string Id, bool Hidden)> Panels(string html)
        => Regex.Matches(html, "<form[^>]*data-section-panel=\"([^\"]+)\"[^>]*>")
            .Select(m => (m.Groups[1].Value, Regex.IsMatch(m.Value, @"\shidden[=\s>]")))
            .ToList();

    internal static List<(string Id, bool Active)> NavItems(string html)
        => Regex.Matches(html, "<li class=\"([^\"]*)\" data-section-item=\"([^\"]+)\"><a[^>]*aria-current=\"([^\"]+)\"")
            .Select(m =>
            {
                var active = m.Groups[1].Value == "active";
                Assert.Equal(active ? "page" : "false", m.Groups[3].Value);
                return (m.Groups[2].Value, active);
            })
            .ToList();

    private static void AssertOnlyShown(string html, string id)
    {
        var panels = Panels(html);
        Assert.True(panels.Count > 3);
        Assert.Equal(new[] { id }, panels.Where(p => !p.Hidden).Select(p => p.Id));
        Assert.Equal(new[] { id }, NavItems(html).Where(n => n.Active).Select(n => n.Id));
        Assert.Equal(panels.Select(p => p.Id), NavItems(html).Select(n => n.Id));
    }

    [Fact]
    public async Task TheFirstGetShowsOnlyTheGeneralSection()
    {
        var admin = await StartAsync();

        var html = await admin.GetPageAsync("admin/settings");

        AssertOnlyShown(html, "general");
        Assert.Equal("general", Panels(html)[0].Id);
    }

    [Fact]
    public async Task EverySectionHasAUniqueValidId()
    {
        var admin = await StartAsync();

        var ids = Panels(await admin.GetPageAsync("admin/settings")).Select(p => p.Id).ToList();

        Assert.All(ids, id => Assert.Matches("^[A-Za-z][A-Za-z0-9_-]*$", id));
        Assert.Equal(ids.Count, ids.Select(i => i.ToLowerInvariant()).Distinct().Count());
        Assert.Contains("Database", ids);
    }

    [Fact]
    public async Task TheSectionQueryShowsThatSectionOnly()
    {
        var admin = await StartAsync();

        AssertOnlyShown(await admin.GetPageAsync("admin/settings?section=Database"), "Database");
        AssertOnlyShown(await admin.GetPageAsync("admin/settings?section=database"), "Database");
    }

    [Fact]
    public async Task AnUnknownSectionShowsTheFirst()
    {
        var admin = await StartAsync();

        AssertOnlyShown(await admin.GetPageAsync("admin/settings?section=nope"), "general");
    }

    [Fact]
    public async Task TheNavLinksReachEverySectionWithoutJavaScript()
    {
        var admin = await StartAsync();
        var html = await admin.GetPageAsync("admin/settings");

        Assert.True(NavItems(html).Count > 3);
        foreach (var (id, _) in NavItems(html))
        {
            Assert.Contains($"href=\"?section={id}#{id}\"", html);
        }
    }

    [Fact]
    public async Task AFailedSaveShowsTheFailingSection()
    {
        var admin = await StartAsync();

        using var response = await admin.PostAsync("admin/settings", new Dictionary<string, string> { ["section"] = "Database", ["password"] = "wrong" });
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        AssertOnlyShown(html, "Database");
        Assert.Contains("data-locked=\"true\"", html);
        Assert.Contains("confirmation is incorrect", html);
    }

    [Fact]
    public async Task AFailedSaveOfTheGeneralSectionShowsGeneral()
    {
        var admin = await StartAsync();

        using var response = await admin.PostAsync("admin/settings", new Dictionary<string, string> { ["section"] = "", ["password"] = "wrong" });

        AssertOnlyShown(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), "general");
    }

    [Theory]
    [InlineData("", "general")]
    [InlineData("Database", "Database")]
    public async Task ASuccessfulSaveRedirectsToTheSectionId(string section, string id)
    {
        var admin = await StartAsync();

        using var response = await admin.PostAsync("admin/settings", new Dictionary<string, string> { ["section"] = section, ["password"] = Password });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.EndsWith("#" + id, location);
        Assert.Contains("section=" + id, location);

        var html = await admin.GetPageAsync(location.Substring(location.IndexOf("admin/settings", StringComparison.Ordinal)));
        AssertOnlyShown(html, id);
    }
}
