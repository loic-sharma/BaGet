using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The V3 protocol contract. Abstract: it runs once against the default repository at <c>/</c>
/// (<see cref="ApiIntegrationTests"/>) and once against a second repository at <c>/r/second/</c>
/// (<see cref="ApiIntegrationSecondRepositoryTests"/>); every expectation is identical except the advertised base URL.
/// </summary>
public abstract class ApiIntegrationTestsBase : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    private readonly Stream? _packageStream;
    private readonly Stream? _symbolPackageStream;

    private readonly string? _repository;
    private readonly string _origin;

    protected ApiIntegrationTestsBase(ITestOutputHelper output, string? repository)
    {
        _repository = repository;
        _origin = repository == null ? "http://localhost/" : $"http://localhost/r/{repository}/";
        _app = new BaGetApplication(output);
        _client = _app.CreateClient();
        if (repository != null)
        {
            _app.CreateRepositoryAsync(repository).GetAwaiter().GetResult();
        }

        // Relative request URIs resolve against the repository's root.
        _client.BaseAddress = new Uri(_origin);

        _packageStream = TestResources.GetResourceStream(TestResources.Package);
        _symbolPackageStream = TestResources.GetResourceStream(TestResources.SymbolPackage);
    }

    /// <summary>Rewrites the expected absolute URLs (written for <c>/</c>) to this repository's root.</summary>
    private string Expect(string expected) => expected.Replace("http://localhost/", _origin).ReplaceLineEndings();

    [Fact]
    public async Task IndexReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/index.json", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(TestData.ServiceIndex), content);
    }

    [Fact]
    public async Task SearchReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("v3/search", cancellationToken);
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration""
  },
  ""totalHits"": 1,
  ""data"": [
    {
      ""id"": ""TestData"",
      ""version"": ""1.2.3"",
      ""description"": ""Test description"",
      ""authors"": [
        ""Test author""
      ],
      ""iconUrl"": """",
      ""licenseUrl"": """",
      ""projectUrl"": """",
      ""registration"": ""http://localhost/v3/registration/testdata/index.json"",
      ""summary"": """",
      ""tags"": [],
      ""title"": """",
      ""totalDownloads"": 0,
      ""versions"": [
        {
          ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
          ""version"": ""1.2.3"",
          ""downloads"": 0
        }
      ]
    }
  ]
}"), json);
    }

    [Fact]
    public async Task SearchReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/search?q=PackageDoesNotExist", cancellationToken);
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration""
  },
  ""totalHits"": 0,
  ""data"": []
}"), json);
    }

    [Fact]
    public async Task AutocompleteReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("v3/autocomplete", cancellationToken);
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 1,
  ""data"": [
    ""TestData""
  ]
}"), json);
    }

    [Fact]
    public async Task AutocompleteReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/autocomplete?q=PackageDoesNotExist", cancellationToken);
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 0,
  ""data"": []
}"), json);
    }

    [Fact]
    public async Task AutocompleteVersionsReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("v3/autocomplete?id=TestData", cancellationToken);
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 1,
  ""data"": [
    ""1.2.3""
  ]
}"), json);
    }

    [Fact]
    public async Task AutocompleteVersionsReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/autocomplete?id=PackageDoesNotExist", cancellationToken);
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 0,
  ""data"": []
}"), json);
    }

    [Fact]
    public async Task VersionListReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        var response = await _client.GetAsync("v3/package/TestData/index.json", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{""versions"":[""1.2.3""]}", content);
    }

    [Fact]
    public async Task VersionListReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/package/PackageDoesNotExist/index.json", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageDownloadReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("v3/package/TestData/1.2.3/TestData.1.2.3.nupkg", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PackageDownloadReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync(
            "v3/package/PackageDoesNotExist/1.0.0/PackageDoesNotExist.1.0.0.nupkg", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NuspecDownloadReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync(
            "v3/package/TestData/1.2.3/TestData.1.2.3.nuspec", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NuspecDownloadReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync(
            "v3/package/PackageDoesNotExist/1.0.0/PackageDoesNotExist.1.0.0.nuspec", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageMetadataReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("v3/registration/TestData/index.json", cancellationToken);
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""@id"": ""http://localhost/v3/registration/testdata/index.json"",
  ""@type"": [
    ""catalog:CatalogRoot"",
    ""PackageRegistration"",
    ""catalog:Permalink""
  ],
  ""count"": 1,
  ""items"": [
    {
      ""@id"": ""http://localhost/v3/registration/testdata/index.json"",
      ""count"": 1,
      ""lower"": ""1.2.3"",
      ""upper"": ""1.2.3"",
      ""items"": [
        {
          ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
          ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
          ""catalogEntry"": {
            ""downloads"": 0,
            ""hasReadme"": false,
            ""packageTypes"": [
              ""Dependency""
            ],
            ""releaseNotes"": """",
            ""repositoryUrl"": """",
            ""id"": ""TestData"",
            ""version"": ""1.2.3"",
            ""authors"": ""Test author"",
            ""dependencyGroups"": [
              {
                ""targetFramework"": ""net5.0"",
                ""dependencies"": []
              }
            ],
            ""description"": ""Test description"",
            ""iconUrl"": """",
            ""language"": """",
            ""licenseUrl"": """",
            ""listed"": true,
            ""minClientVersion"": """",
            ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
            ""projectUrl"": """",
            ""published"": ""2020-01-01T00:00:00Z"",
            ""requireLicenseAcceptance"": false,
            ""summary"": """",
            ""tags"": [],
            ""title"": """"
          }
        }
      ]
    }
  ],
  ""totalDownloads"": 0
}"), json);
    }

    [Fact]
    public async Task PackageMetadataReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/registration/PackageDoesNotExist/index.json", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageMetadataLeafReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync("v3/registration/TestData/1.2.3.json", cancellationToken);
        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
  ""@type"": [
    ""Package"",
    ""http://schema.nuget.org/catalog#Permalink""
  ],
  ""listed"": true,
  ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
  ""published"": ""2020-01-01T00:00:00Z"",
  ""registration"": ""http://localhost/v3/registration/testdata/index.json""
}"), json);
    }

    [Fact]
    public async Task PackageMetadataLeafReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/registration/PackageDoesNotExist/1.0.0.json", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageDependentsReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/dependents?packageId=TestData", cancellationToken);

        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Expect(@"{
  ""totalHits"": 0,
  ""data"": []
}"), json);
    }

    [Fact]
    public async Task PackageDependentsReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("v3/dependents", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SymbolDownloadReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);
        await _app.AddSymbolPackageAsync(_symbolPackageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync(
            "api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981B1/testdata.pdb")]
    [InlineData("api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981B/testdata.pdb")]
    [InlineData("api/download/symbols/testprefix/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb")]
    public async Task MalformedSymbolDownloadReturnsOk(string uri)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _app.AddPackageAsync(_packageStream, cancellationToken, _repository);
        await _app.AddSymbolPackageAsync(_symbolPackageStream, cancellationToken, _repository);

        using var response = await _client.GetAsync(uri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SymbolDownloadReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync(
            "api/download/symbols/doesnotexist.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/doesnotexist.pdb", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _app.Dispose();
        _client.Dispose();
    }
}

public class ApiIntegrationTests(ITestOutputHelper output) : ApiIntegrationTestsBase(output, null);

public class ApiIntegrationSecondRepositoryTests(ITestOutputHelper output) : ApiIntegrationTestsBase(output, "second");
