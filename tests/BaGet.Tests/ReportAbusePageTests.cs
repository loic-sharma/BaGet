using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The service index advertises a ReportAbuseUriTemplate, so the address a client
/// expands that template into has to resolve to a page rather than a 404.
/// </summary>
public class ReportAbusePageTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public ReportAbusePageTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output);
        _client = _app.CreateDefaultClient();
    }

    [Fact]
    public async Task TheExpandedTemplateResolves()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("packages/TestData/1.2.3/report-abuse", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ThePageNamesThePackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("packages/TestData/1.2.3/report-abuse", cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("TestData", content);
        Assert.Contains("1.2.3", content);
    }

    [Fact]
    public async Task ItResolvesForAPackageThatDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // A client expands the template from whatever it is looking at, so the page
        // must not depend on the package being present.
        using var response = await _client.GetAsync("packages/No.Such.Package/9.9.9/report-abuse", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheVersionLessUrlIsNotTheReportAbusePage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The package page owns "/packages/{id}/{version?}", so a three segment URL
        // is read as that page with "report-abuse" as the version, not as this page.
        // The template always supplies a version, so nothing expands to it.
        using var response = await _client.GetAsync("packages/TestData/report-abuse", cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("Report abuse", content);
    }

    [Fact]
    public async Task APostedSubmittedFieldDoesNotClaimTheReportWasRecorded()
    {
        // The page's Submitted had a private setter; the view model's must not be bound from the form either.
        using var browser = new Browser(_app);
        using var response = await browser.PostAsync("packages/TestData/1.2.3/report-abuse", new Dictionary<string, string>
        {
            { "Reason", "" },
            { "Details", "" },
            { "Submitted", "true" },
            { "CanReport", "true" },
        });

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("report-submitted", content);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _app?.Dispose();
    }
}