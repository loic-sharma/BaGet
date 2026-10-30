using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Every protocol endpoint is served at <c>/</c> (the default repository) and at <c>/r/{repo}/</c> (the named one);
/// an unknown repository is 404 and never the default; every URL a repository advertises stays inside it.
/// </summary>
public class RepositoryRoutingTests : IDisposable
{
    private const string ApiKey = "routing-key";
    private const string Second = "second";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public RepositoryRoutingTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", ApiKey },
        });
        _client = _app.CreateClient();
        CreateSecondAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private async Task CreateSecondAsync()
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(Second, RepositoryFormat.NuGet, RepositoryKind.Local, CancellationToken.None);
    }

    /// <summary>Indexes the checked-in test package into the given repository (null = default).</summary>
    private async Task IndexAsync(string? repository)
    {
        using var package = TestResources.GetResourceStream(TestResources.Package)!;
        using var scope = await ScopeAsync(repository);
        var result = await scope.ServiceProvider.GetRequiredService<IPackageIndexingService>()
            .IndexAsync(package, CancellationToken.None);
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    private async Task<IServiceScope> ScopeAsync(string? repository)
    {
        var scope = _app.Services.CreateScope();
        if (repository != null)
        {
            var found = await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .FindAsync(repository, CancellationToken.None);
            scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(found!);
        }

        return scope;
    }

    private static string Prefixed(string path, string? repository)
        => repository == null ? path : $"r/{repository}/{path}";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-NuGet-ApiKey", ApiKey);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // ---- Each endpoint answers the same at "/" and at "/r/second/" ----

    public static TheoryData<string, HttpStatusCode> ReadEndpoints => new()
    {
        { "v3/index.json", HttpStatusCode.OK },
        { "v3/search?q=", HttpStatusCode.OK },
        { "v3/autocomplete?q=Test", HttpStatusCode.OK },
        { "v3/dependents?packageId=TestData", HttpStatusCode.OK },
        { "v3/registration/testdata/index.json", HttpStatusCode.OK },
        { "v3/registration/testdata/1.2.3.json", HttpStatusCode.OK },
        { "v3/package/testdata/index.json", HttpStatusCode.OK },
        { "v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", HttpStatusCode.OK },
        { "v3/package/testdata/1.2.3/testdata.nuspec", HttpStatusCode.OK },
        { "v3/package/testdata/1.2.3/icon", HttpStatusCode.NotFound },
        { "v3/package/testdata/1.2.3/readme", HttpStatusCode.NotFound },
        { "v3/vulnerabilities/index.json", HttpStatusCode.OK },
        { "v3/vulnerabilities/base.json", HttpStatusCode.OK },
        { "v3/catalog/index.json", HttpStatusCode.OK },
        { "api/v2", HttpStatusCode.OK },
        { "api/v2/$metadata", HttpStatusCode.OK },
        { "api/v2/Packages()", HttpStatusCode.OK },
        { "api/v2/FindPackagesById()?id='TestData'", HttpStatusCode.OK },
        { "api/v2/Search()?searchTerm='Test'", HttpStatusCode.OK },
        { "api/v2/Packages(Id='TestData',Version='1.2.3')", HttpStatusCode.OK },
        { "api/download/symbols/missing.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/missing.pdb", HttpStatusCode.NotFound },
        { "api/download/symbols/pre/missing.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/missing.pdb", HttpStatusCode.NotFound },
    };

    [Theory]
    [MemberData(nameof(ReadEndpoints))]
    public async Task AReadEndpointAnswersAtTheRootAndUnderTheRepository(string path, HttpStatusCode expected)
    {
        await IndexAsync(null);
        await IndexAsync(Second);

        using var root = await _client.GetAsync(path, TestContext.Current.CancellationToken);
        using var repository = await _client.GetAsync(Prefixed(path, Second), TestContext.Current.CancellationToken);

        Assert.Equal(expected, root.StatusCode);
        Assert.Equal(expected, repository.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ReadEndpoints))]
    public async Task AnUnknownRepositoryIsNotFoundOnEveryEndpoint(string path, HttpStatusCode _)
    {
        await IndexAsync(null);

        using var response = await _client.GetAsync(Prefixed(path, "nope"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownRepositoryNeverFallsBackToTheDefaultRepositoryOnAWrite()
    {
        using var package = TestPackages.Build("Stray.Package", "1.0.0");
        using var request = TestPackages.PushRequest(package, ApiKey);
        request.RequestUri = new Uri("r/nope/api/v2/package", UriKind.Relative);

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var root = await _client.GetAsync("v3/package/stray.package/index.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
    }

    [Fact]
    public async Task TheDefaultRepositoryIsAlsoReachableByName()
    {
        await IndexAsync(null);

        using var response = await _client.GetAsync("r/default/v3/package/testdata/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheRepositoryNameIsCaseInsensitive()
    {
        await IndexAsync(Second);

        using var response = await _client.GetAsync("r/Second/v3/package/testdata/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Repositories are separate feeds behind the same routes ----

    [Fact]
    public async Task APackageInOneRepositoryIsInvisibleInTheOther()
    {
        await IndexAsync(Second);
        var token = TestContext.Current.CancellationToken;

        using var inSecond = await _client.GetAsync("r/second/v3/package/testdata/index.json", token);
        using var atRoot = await _client.GetAsync("v3/package/testdata/index.json", token);
        var search = JsonDocument.Parse(await _client.GetStringAsync("v3/search?q=", token));

        Assert.Equal(HttpStatusCode.OK, inSecond.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, atRoot.StatusCode);
        Assert.Equal(0, search.RootElement.GetProperty("totalHits").GetInt32());
    }

    [Fact]
    public async Task APushDeprecationAndDeleteThroughARepositoryTouchOnlyThatRepository()
    {
        var token = TestContext.Current.CancellationToken;

        using (var package = TestPackages.Build("Only.Second", "1.0.0"))
        using (var push = TestPackages.PushRequest(package, ApiKey))
        {
            push.RequestUri = new Uri("r/second/api/v2/package", UriKind.Relative);
            using var pushed = await _client.SendAsync(push, token);
            Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);
        }

        using var inSecond = await _client.GetAsync("r/second/v3/package/only.second/index.json", token);
        using var atRoot = await _client.GetAsync("v3/package/only.second/index.json", token);
        Assert.Equal(HttpStatusCode.OK, inSecond.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, atRoot.StatusCode);

        // The same ID and version can be pushed to the other repository.
        using (var package = TestPackages.Build("Only.Second", "1.0.0"))
        using (var push = TestPackages.PushRequest(package, ApiKey))
        {
            using var pushed = await _client.SendAsync(push, token);
            Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);
        }

        using var deprecate = await SendAsync(HttpMethod.Put, "r/second/v3/package/Only.Second/1.0.0/deprecation",
            @"{""reasons"":[""Legacy""],""message"":""old""}");
        Assert.Equal(HttpStatusCode.NoContent, deprecate.StatusCode);

        var secondRegistration = await _client.GetStringAsync("r/second/v3/registration/only.second/index.json", token);
        var rootRegistration = await _client.GetStringAsync("v3/registration/only.second/index.json", token);
        Assert.Contains("deprecation", secondRegistration);
        Assert.DoesNotContain("deprecation", rootRegistration);

        // Delete unlists (the default behaviour): the second repository's copy leaves its search, the default's stays.
        using var deleted = await SendAsync(HttpMethod.Delete, "r/second/api/v2/package/Only.Second/1.0.0");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var secondHits = JsonDocument.Parse(await _client.GetStringAsync("r/second/v3/search?q=only", token));
        var rootHits = JsonDocument.Parse(await _client.GetStringAsync("v3/search?q=only", token));
        Assert.Equal(0, secondHits.RootElement.GetProperty("totalHits").GetInt32());
        Assert.Equal(1, rootHits.RootElement.GetProperty("totalHits").GetInt32());
    }

    [Fact]
    public async Task SymbolsAreServedUnderARepository()
    {
        var token = TestContext.Current.CancellationToken;
        const string path = "api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb";

        using (var scope = await ScopeAsync(Second))
        using (var package = TestResources.GetResourceStream(TestResources.Package)!)
        using (var symbols = TestResources.GetResourceStream(TestResources.SymbolPackage)!)
        {
            await scope.ServiceProvider.GetRequiredService<IPackageIndexingService>().IndexAsync(package, token);
            var result = await scope.ServiceProvider.GetRequiredService<ISymbolIndexingService>().IndexAsync(symbols, token);
            Assert.Equal(SymbolIndexingResult.Success, result);
        }

        using var inSecond = await _client.GetAsync("r/second/" + path, token);

        Assert.Equal(HttpStatusCode.OK, inSecond.StatusCode);
    }

    // ---- Advertised URLs stay inside the repository ----

    [Fact]
    public async Task TheServiceIndexOfARepositoryAdvertisesOnlyThatRepository()
    {
        var ids = await ResourceIdsAsync("r/second/v3/index.json", TestContext.Current.CancellationToken);

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.StartsWith("http://localhost/r/second/", id));
    }

    [Fact]
    public async Task TheDefaultServiceIndexAdvertisesNoRepositoryPrefix()
    {
        var ids = await ResourceIdsAsync("v3/index.json", TestContext.Current.CancellationToken);

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.DoesNotContain("/r/", id));
    }

    [Fact]
    public async Task EveryIndexAdvertisesThePackagePagesOfItsOwnRepository()
    {
        var token = TestContext.Current.CancellationToken;

        var defaultTypes = await ResourceTypesAsync("v3/index.json", token);
        var secondTypes = await ResourceTypesAsync("r/second/v3/index.json", token);

        Assert.Contains(defaultTypes, t => t.StartsWith("PackageDetailsUriTemplate"));
        Assert.Contains(defaultTypes, t => t.StartsWith("ReportAbuseUriTemplate"));
        Assert.Contains(secondTypes, t => t.StartsWith("PackageDetailsUriTemplate"));
        Assert.Contains(secondTypes, t => t.StartsWith("ReportAbuseUriTemplate"));
        Assert.Contains(secondTypes, t => t.StartsWith("PackageBaseAddress"));

        // The advertised address is a page of the same repository.
        var secondIds = await ResourceIdsAsync("r/second/v3/index.json", token);
        Assert.Contains("http://localhost/r/second/packages/{id}/{version}", secondIds);
        Assert.Contains("http://localhost/r/second/packages/{id}/{version}/report-abuse", secondIds);
    }

    [Fact]
    public async Task PagesLinkingBetweenPackagesStillRenderTheirLinks()
    {
        await IndexAsync(null);

        var page = await _client.GetStringAsync("packages/TestData/1.2.3", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("href=\"\"", page);
    }

    [Fact]
    public async Task EveryUrlInARepositorysSearchAndRegistrationStaysInThatRepository()
    {
        var token = TestContext.Current.CancellationToken;
        await IndexAsync(Second);

        var search = await _client.GetStringAsync("r/second/v3/search?q=", token);
        var registration = await _client.GetStringAsync("r/second/v3/registration/testdata/index.json", token);

        var urls = Urls(search).Concat(Urls(registration)).ToList();
        Assert.NotEmpty(Urls(search));
        Assert.NotEmpty(Urls(registration));
        Assert.All(urls, url => Assert.StartsWith("http://localhost/r/second/", url));
    }

    [Fact]
    public async Task TheV2FeedOfARepositoryLinksInsideThatRepository()
    {
        var token = TestContext.Current.CancellationToken;
        await IndexAsync(Second);

        var feed = await _client.GetStringAsync("r/second/api/v2/Packages()", token);
        var service = await _client.GetStringAsync("r/second/api/v2", token);

        var document = XDocument.Parse(feed);
        Assert.Equal("http://localhost/r/second/api/v2", document.Root!.Attribute(XNamespace.Xml + "base")!.Value);
        Assert.Single(document.Root.Elements("{http://www.w3.org/2005/Atom}entry"));
        Assert.Contains("xml:base=\"http://localhost/r/second/api/v2\"", service);
        Assert.DoesNotContain("http://localhost/api/v2", feed);
        Assert.Contains("http://localhost/r/second/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg", feed);
    }

    // ---- A real NuGet client against the repository's service index ----

    private SourceRepository SourceAt(string indexPath)
    {
        var providers = new List<Lazy<INuGetResourceProvider>>
        {
            new(() => new HttpSourceResourceProviderTestHost(_app.CreateDefaultClient())),
        };
        providers.AddRange(NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3());

        return new SourceRepository(new PackageSource(new Uri(_app.Server.BaseAddress, indexPath).AbsoluteUri), providers);
    }

    [Fact]
    public async Task TheNuGetClientRestoresAndSearchesThroughARepositoryIndex()
    {
        var token = TestContext.Current.CancellationToken;
        await IndexAsync(Second);
        var source = SourceAt("r/second/v3/index.json");
        using var cache = new SourceCacheContext { NoCache = true, DirectDownload = true };
        var log = NuGet.Common.NullLogger.Instance;

        var find = (await source.GetResourceAsync<FindPackageByIdResource>(token))!;
        var versions = await find.GetAllVersionsAsync("TestData", cache, log, token);
        using var nupkg = new MemoryStream();
        var copied = await find.CopyNupkgToStreamAsync("TestData", NuGetVersion.Parse("1.2.3"), nupkg, cache, log, token);

        var search = (await source.GetResourceAsync<PackageSearchResource>(token))!;
        var found = await search.SearchAsync("", new SearchFilter(includePrerelease: true), 0, 10, log, token);

        Assert.Equal(new[] { NuGetVersion.Parse("1.2.3") }, versions.ToArray());
        Assert.True(copied);
        Assert.True(nupkg.Length > 0);
        Assert.Single(found);

        // The same package is not in the default repository.
        var rootFind = (await SourceAt("v3/index.json").GetResourceAsync<FindPackageByIdResource>(token))!;
        Assert.Empty(await rootFind.GetAllVersionsAsync("TestData", cache, log, token));
    }

    [Fact]
    public async Task ThePublishUrlAdvertisedByARepositoryPushesIntoThatRepository()
    {
        var token = TestContext.Current.CancellationToken;
        var index = JsonDocument.Parse(await _client.GetStringAsync("r/second/v3/index.json", token));
        var publish = index.RootElement.GetProperty("resources").EnumerateArray()
            .First(r => r.GetProperty("@type").GetString() == "PackagePublish/2.0.0")
            .GetProperty("@id").GetString()!;
        Assert.Equal("http://localhost/r/second/api/v2/package", publish);

        using (var package = TestPackages.Build("Pushed.ByClient", "2.0.0"))
        using (var push = TestPackages.PushRequest(package, ApiKey))
        {
            push.RequestUri = new Uri(publish);
            using var pushed = await _client.SendAsync(push, token);
            Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);
        }

        using var inSecond = await _client.GetAsync("r/second/v3/package/pushed.byclient/index.json", token);
        using var atRoot = await _client.GetAsync("v3/package/pushed.byclient/index.json", token);
        Assert.Equal(HttpStatusCode.OK, inSecond.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, atRoot.StatusCode);
    }

    private async Task<List<string>> ResourceTypesAsync(string path, CancellationToken token)
    {
        var index = JsonDocument.Parse(await _client.GetStringAsync(path, token));

        return index.RootElement.GetProperty("resources").EnumerateArray()
            .Select(r => r.GetProperty("@type").GetString()!)
            .ToList();
    }

    private async Task<List<string>> ResourceIdsAsync(string path, CancellationToken token)
    {
        var index = JsonDocument.Parse(await _client.GetStringAsync(path, token));

        return index.RootElement.GetProperty("resources").EnumerateArray()
            .Select(r => r.GetProperty("@id").GetString()!)
            .ToList();
    }

    /// <summary>Every absolute BaGet URL value in a JSON document.</summary>
    private static List<string> Urls(string json)
    {
        var urls = new List<string>();

        void Walk(JsonElement element, string? name)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in element.EnumerateObject()) Walk(p.Value, p.Name);
                    break;
                case JsonValueKind.Array:
                    foreach (var e in element.EnumerateArray()) Walk(e, name);
                    break;
                case JsonValueKind.String:
                    var s = element.GetString()!;
                    // iconUrl/licenseUrl/projectUrl are package content, not BaGet URLs.
                    if (s.StartsWith("http://localhost/") && name is not ("iconUrl" or "licenseUrl" or "projectUrl"))
                    {
                        urls.Add(s);
                    }

                    break;
            }
        }

        Walk(JsonDocument.Parse(json).RootElement, null);
        return urls;
    }
}

/// <summary>
/// A configured path base comes before the <c>r/{repo}/</c> prefix in every advertised URL.
/// </summary>
public class RepositoryRoutingPathBaseTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public RepositoryRoutingPathBaseTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string> { { "PathBase", "/nuget" } });
        _client = _app.CreateClient();
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    [Theory]
    [InlineData("r/second/v3/index.json")]
    [InlineData("nuget/r/second/v3/index.json")]
    public async Task TheServiceIndexKeepsThePathBaseAndTheRepository(string path)
    {
        var token = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync(path, token);
        response.EnsureSuccessStatusCode();

        var index = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var ids = index.RootElement.GetProperty("resources").EnumerateArray()
            .Select(r => r.GetProperty("@id").GetString()!).ToList();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.StartsWith("http://localhost/nuget/r/second/", id));
    }

    [Fact]
    public async Task TheV2FeedKeepsThePathBaseAndTheRepository()
    {
        var service = await _client.GetStringAsync("r/second/api/v2", TestContext.Current.CancellationToken);

        Assert.Contains("xml:base=\"http://localhost/nuget/r/second/api/v2\"", service);
    }
}
