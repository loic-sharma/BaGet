using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Deprecation and vulnerability metadata: set through the admin endpoints, then read
/// back through registration and the VulnerabilityInfo resource.
/// </summary>
public class PackageMetadataAdminTests : IDisposable
{
    private const string ApiKey = "secret";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;
    private readonly Stream? _packageStream;

    public PackageMetadataAdminTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", ApiKey },
        });
        _client = _app.CreateClient();
        _packageStream = TestResources.GetResourceStream(TestResources.Package);
    }

    [Fact]
    public async Task ADeprecationAppearsInRegistration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken);

        using var put = await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/deprecation",
            @"{""reasons"":[""Legacy""],""message"":""Use Other"",""alternatePackageId"":""Other""}");
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var entry = await CatalogEntryAsync();
        var deprecation = entry.GetProperty("deprecation");

        Assert.Equal("Legacy", deprecation.GetProperty("reasons")[0].GetString());
        Assert.Equal("Use Other", deprecation.GetProperty("message").GetString());
        Assert.Equal("Other", deprecation.GetProperty("alternatePackage").GetProperty("id").GetString());
        Assert.Equal("*", deprecation.GetProperty("alternatePackage").GetProperty("range").GetString());
    }

    [Fact]
    public async Task RemovingTheDeprecationClearsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken);

        (await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/deprecation", @"{""reasons"":[""Other""]}")).Dispose();
        using var delete = await SendAsync(HttpMethod.Delete, "v3/package/TestData/1.2.3/deprecation");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.False((await CatalogEntryAsync()).TryGetProperty("deprecation", out _));
    }

    [Fact]
    public async Task AnUnknownReasonIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken);

        using var put = await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/deprecation", @"{""reasons"":[""Bored""]}");

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task AMissingPackageIsNotFound()
    {
        using var put = await SendAsync(HttpMethod.Put, "v3/package/No.Such/1.0.0/deprecation", @"{""reasons"":[""Legacy""]}");

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task AWrongApiKeyIsUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Put, "v3/package/TestData/1.2.3/deprecation")
        {
            Content = new StringContent(@"{""reasons"":[""Legacy""]}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-NuGet-ApiKey", "wrong");

        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VulnerabilitiesAppearInRegistrationAndTheVulnerabilityPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken);

        using var put = await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/vulnerabilities",
            @"[{""advisoryUrl"":""https://example.test/advisory/1"",""severity"":2}]");
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var vulnerability = (await CatalogEntryAsync()).GetProperty("vulnerabilities")[0];
        Assert.Equal("https://example.test/advisory/1", vulnerability.GetProperty("advisoryUrl").GetString());
        Assert.Equal("2", vulnerability.GetProperty("severity").GetString());

        using var index = await GetJsonAsync("v3/vulnerabilities/index.json");
        var pageUrl = index.RootElement[0].GetProperty("@id").GetString();
        Assert.Equal("base", index.RootElement[0].GetProperty("@name").GetString());

        using var page = await GetJsonAsync(pageUrl);
        var item = page.RootElement.GetProperty("testdata")[0];
        Assert.Equal(2, item.GetProperty("severity").GetInt32());
        Assert.Equal("https://example.test/advisory/1", item.GetProperty("url").GetString());
        Assert.Equal("[1.2.3]", item.GetProperty("versions").GetString());
    }

    [Fact]
    public async Task AnEmptyListClearsTheVulnerabilities()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken);

        (await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/vulnerabilities",
            @"[{""advisoryUrl"":""https://example.test/a"",""severity"":1}]")).Dispose();
        using var clear = await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/vulnerabilities", "[]");

        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
        Assert.False((await CatalogEntryAsync()).TryGetProperty("vulnerabilities", out _));

        using var page = await GetJsonAsync("v3/vulnerabilities/base.json");
        Assert.Empty(page.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task AnOutOfRangeSeverityIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken);

        using var put = await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/vulnerabilities",
            @"[{""advisoryUrl"":""https://example.test/a"",""severity"":4}]");

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task SearchCarriesTheLatestVersionsMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken);

        (await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/deprecation", @"{""reasons"":[""CriticalBugs""]}")).Dispose();
        (await SendAsync(HttpMethod.Put, "v3/package/TestData/1.2.3/vulnerabilities",
            @"[{""advisoryUrl"":""https://example.test/a"",""severity"":3}]")).Dispose();

        using var search = await GetJsonAsync("v3/search");
        var result = search.RootElement.GetProperty("data")[0];

        Assert.Equal("CriticalBugs", result.GetProperty("deprecation").GetProperty("reasons")[0].GetString());
        Assert.Equal("3", result.GetProperty("vulnerabilities")[0].GetProperty("severity").GetString());
    }

    [Fact]
    public async Task AnUnknownVulnerabilityPageIsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/vulnerabilities/other.json", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-NuGet-ApiKey", ApiKey);
        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await _client.SendAsync(request);
    }

    private async Task<JsonDocument> GetJsonAsync(string? url)
    {
        using var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> CatalogEntryAsync()
    {
        using var index = await GetJsonAsync("v3/registration/testdata/index.json");

        return index.RootElement
            .GetProperty("items")[0]
            .GetProperty("items")[0]
            .GetProperty("catalogEntry")
            .Clone();
    }

    public void Dispose()
    {
        _app.Dispose();
        _client.Dispose();
        #pragma warning disable CS8602
        _packageStream.Dispose();
        #pragma warning restore CS8602
    }
}