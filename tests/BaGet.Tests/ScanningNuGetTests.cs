using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Vulnerability scanning of NuGet repositories over HTTP (P7): a package and the lowest version of each dependency range are
/// checked against a fake OSV, what is found about the package itself feeds NuGet's vulnerability resource and registration, and
/// the policy blocks pushes and downloads with a 403 and the reason.
/// </summary>
public class ScanningNuGetTests
{
    private const int Low = 0, Moderate = 1, High = 2, Critical = 3;

    private readonly ITestOutputHelper _output;

    public ScanningNuGetTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static async Task<HttpResponseMessage> PushAsync(ScanningHarness harness, string id, string version, string repository = "", params (string Id, string Range)[] dependencies)
    {
        using var package = TestPackages.Build(id, version, dependencies: dependencies);
        using var request = TestPackages.PushRequest(package, ScanningHarness.Key);
        request.RequestUri = new System.Uri(repository + "api/v2/package", System.UriKind.Relative);

        return await harness.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> DownloadAsync(ScanningHarness harness, string id, string version, string repository = "")
        => harness.Client.GetAsync($"{repository}v3/package/{id.ToLowerInvariant()}/{version}/{id.ToLowerInvariant()}.{version}.nupkg", TestContext.Current.CancellationToken);

    private static async Task<JsonDocument> JsonAsync(ScanningHarness harness, string uri)
        => JsonDocument.Parse(await harness.Client.GetStringAsync(uri, TestContext.Current.CancellationToken));

    private static async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task APackageAndTheLowestVersionOfItsDependencyRangesAreScanned()
    {
        await using var harness = await ScanningHarness.StartAsync(_output);
        harness.Osv.Add("NuGet", "Newtonsoft.Json", "9.0.1", "GHSA-json", "HIGH", "13.0.1");
        Assert.Equal(HttpStatusCode.Created, (await PushAsync(harness, "Acme.App", "1.0.0", dependencies: new[] { ("Newtonsoft.Json", "[9.0.1,)"), ("Unbounded", "(,2.0)") })).StatusCode);

        var result = (await harness.ScanAsync(null, "Acme.App", "1.0.0"))!;

        Assert.Equal(High, result.Highest);
        var found = (await harness.ResultAsync(null, "acme.app", "1.0.0"))!;
        Assert.Equal("Newtonsoft.Json", found.Findings!.Single().Component);
        Assert.Equal("9.0.1", found.Findings!.Single().ComponentVersion);
        // The package itself and the one dependency with a lower bound; "Unbounded" has none.
        Assert.Equal(2, harness.Osv.QueriedPackages);
    }

    [Fact]
    public async Task WhatIsFoundAboutThePackageItselfFeedsTheVulnerabilityResourceAndRegistration()
    {
        await using var harness = await ScanningHarness.StartAsync(_output);
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-own", "CRITICAL");
        harness.Osv.Add("NuGet", "Other", "3.0.0", "GHSA-dependency", "CRITICAL");
        await PushAsync(harness, "Acme.App", "1.0.0", dependencies: new[] { ("Other", "[3.0.0]") });

        await harness.ScanAsync(null, "Acme.App", "1.0.0");

        using var index = await JsonAsync(harness, "v3/vulnerabilities/index.json");
        using var page = await JsonAsync(harness, index.RootElement[0].GetProperty("@id").GetString()!);
        var items = page.RootElement.GetProperty("acme.app");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(Critical, items[0].GetProperty("severity").GetInt32());
        Assert.Equal("https://example.test/advisories/GHSA-own", items[0].GetProperty("url").GetString());
        Assert.Equal("[1.0.0]", items[0].GetProperty("versions").GetString());
        // A dependency's advisory is that package's business: the client matches its restore graph against the resource.
        Assert.False(page.RootElement.TryGetProperty("other", out _));

        using var registration = await JsonAsync(harness, "v3/registration/acme.app/index.json");
        var entry = registration.RootElement.GetProperty("items")[0].GetProperty("items")[0].GetProperty("catalogEntry");
        Assert.Equal("https://example.test/advisories/GHSA-own", entry.GetProperty("vulnerabilities")[0].GetProperty("advisoryUrl").GetString());
    }

    [Fact]
    public async Task SettingTheListByHandKeepsTheScannersEntriesAndAScanKeepsTheHandOnes()
    {
        await using var harness = await ScanningHarness.StartAsync(_output);
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-own", "HIGH");
        await PushAsync(harness, "Acme.App", "1.0.0");
        await harness.ScanAsync(null, "Acme.App", "1.0.0");

        using var put = new HttpRequestMessage(HttpMethod.Put, "v3/package/Acme.App/1.0.0/vulnerabilities")
        {
            Content = new StringContent(@"[{""advisoryUrl"":""https://example.test/manual"",""severity"":1}]", Encoding.UTF8, "application/json"),
        };
        put.Headers.Add("X-NuGet-ApiKey", ScanningHarness.Key);
        Assert.Equal(HttpStatusCode.NoContent, (await harness.Client.SendAsync(put, TestContext.Current.CancellationToken)).StatusCode);
        await harness.ScanAsync(null, "Acme.App", "1.0.0");

        var urls = await VulnerabilityUrlsAsync(harness);
        Assert.Equal(new[] { "https://example.test/advisories/GHSA-own", "https://example.test/manual" }, urls);

        // The next scan finds nothing more: the scanner's entry goes, the manual one stays.
        harness.Osv.Clear();
        harness.App.Services.GetRequiredService<OsvState>().Clear();
        await harness.ScanAsync(null, "Acme.App", "1.0.0");
        Assert.Equal(new[] { "https://example.test/manual" }, await VulnerabilityUrlsAsync(harness));

        // Clearing by hand empties the manual entries only.
        using var clear = new HttpRequestMessage(HttpMethod.Put, "v3/package/Acme.App/1.0.0/vulnerabilities") { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        clear.Headers.Add("X-NuGet-ApiKey", ScanningHarness.Key);
        Assert.Equal(HttpStatusCode.NoContent, (await harness.Client.SendAsync(clear, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Empty(await VulnerabilityUrlsAsync(harness));
    }

    private static async Task<List<string>> VulnerabilityUrlsAsync(ScanningHarness harness)
    {
        using var page = await JsonAsync(harness, "v3/vulnerabilities/base.json");
        return page.RootElement.TryGetProperty("acme.app", out var items)
            ? items.EnumerateArray().Select(i => i.GetProperty("url").GetString()!).OrderBy(u => u, System.StringComparer.Ordinal).ToList()
            : new List<string>();
    }

    [Fact]
    public async Task ABlockDeployPolicyRefusesAPushWhoseDependencyIsVulnerableWith403AndTheReason()
    {
        await using var harness = await ScanningHarness.StartAsync(_output);
        harness.Osv.Add("NuGet", "Bad.Lib", "1.0.0", "GHSA-push", "CRITICAL");
        await harness.SetPolicyAsync(null, blockDeploy: High);

        using var refused = await PushAsync(harness, "Acme.App", "1.0.0", dependencies: new[] { ("Bad.Lib", "1.0.0") });
        using var clean = await PushAsync(harness, "Acme.App", "1.0.1", dependencies: new[] { ("Bad.Lib", "2.0.0") });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var reason = await TextAsync(refused);
        Assert.Contains("GHSA-push", reason);
        Assert.Contains("Bad.Lib 1.0.0", reason);
        Assert.Equal(HttpStatusCode.Created, clean.StatusCode);
        using var versions = await JsonAsync(harness, "v3/package/acme.app/index.json");
        Assert.Equal(new[] { "1.0.1" }, versions.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).ToArray());
    }

    [Fact]
    public async Task ABlockDownloadPolicyRefusesTheNupkgOfAVulnerableVersionButNotItsMetadata()
    {
        await using var harness = await ScanningHarness.StartAsync(_output);
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-dl", "HIGH");
        await PushAsync(harness, "Acme.App", "1.0.0");
        await PushAsync(harness, "Acme.App", "2.0.0");
        await harness.ScanAsync(null, "Acme.App", "1.0.0");
        await harness.ScanAsync(null, "Acme.App", "2.0.0");
        await harness.SetPolicyAsync(null, blockDownload: High);

        using var blocked = await DownloadAsync(harness, "Acme.App", "1.0.0");
        using var served = await DownloadAsync(harness, "Acme.App", "2.0.0");
        using var nuspec = await harness.Client.GetAsync("v3/package/acme.app/1.0.0/acme.app.nuspec", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("GHSA-dl", await TextAsync(blocked));
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(HttpStatusCode.OK, nuspec.StatusCode);
    }

    [Fact]
    public async Task ThePolicyOfARepositoryDoesNotReachAnother()
    {
        await using var harness = await ScanningHarness.StartAsync(_output);
        await harness.App.CreateRepositoryAsync("second", TestContext.Current.CancellationToken);
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-iso", "CRITICAL");
        await PushAsync(harness, "Acme.App", "1.0.0");
        await PushAsync(harness, "Acme.App", "1.0.0", "r/second/");
        await harness.ScanAsync(null, "Acme.App", "1.0.0");
        await harness.ScanAsync("second", "Acme.App", "1.0.0");
        await harness.SetPolicyAsync("second", blockDownload: Low);

        using var inDefault = await DownloadAsync(harness, "Acme.App", "1.0.0");
        using var inSecond = await DownloadAsync(harness, "Acme.App", "1.0.0", "r/second/");

        Assert.Equal(HttpStatusCode.OK, inDefault.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inSecond.StatusCode);
        Assert.Contains("'second'", await TextAsync(inSecond));
    }

    [Fact]
    public async Task AVirtualRepositoryAppliesThePolicyOfTheMemberThatHoldsTheVersion()
    {
        await using var harness = await ScanningHarness.StartAsync(_output);
        await harness.App.CreateRepositoryAsync("first", TestContext.Current.CancellationToken);
        await harness.App.CreateRepositoryAsync("second", TestContext.Current.CancellationToken);
        harness.Osv.Add("NuGet", "Acme.App", "1.0.0", "GHSA-virtual", "CRITICAL");
        await PushAsync(harness, "Acme.App", "1.0.0", "r/first/");
        await PushAsync(harness, "Acme.App", "2.0.0", "r/second/");
        await harness.ScanAsync("first", "Acme.App", "1.0.0");
        await harness.ScanAsync("second", "Acme.App", "2.0.0");
        await harness.SetPolicyAsync("first", blockDownload: High);
        await harness.RunAsync(null, sp => sp.GetRequiredService<IVirtualRepositoryAdministration>()
            .CreateAsync("all", new VirtualRepositorySettings { Members = new List<string> { "first", "second" } }, TestContext.Current.CancellationToken));

        using var blocked = await DownloadAsync(harness, "Acme.App", "1.0.0", "r/all/");
        using var served = await DownloadAsync(harness, "Acme.App", "2.0.0", "r/all/");

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("'first'", await TextAsync(blocked));
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Fact]
    public async Task APackageUploadedThroughTheBrowserPageIsRefusedByTheDeployPolicyToo()
    {
        await using var harness = await ScanningHarness.StartAsync(_output);
        harness.Osv.Add("NuGet", "Bad.Lib", "1.0.0", "GHSA-page", "CRITICAL");
        await harness.SetPolicyAsync(null, blockDeploy: Moderate);

        using var browser = new Browser(harness.App);
        using var package = TestPackages.Build("Acme.App", "1.0.0", dependencies: new[] { ("Bad.Lib", "1.0.0") });
        using var response = await browser.StageAsync("Acme.App.1.0.0.nupkg", package, apiKey: ScanningHarness.Key, tokenPage: BrowserUpload.TokenPage);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("GHSA-page", await TextAsync(response));
    }
}
