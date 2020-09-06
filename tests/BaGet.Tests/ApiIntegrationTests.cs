using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    public class ApiIntegrationTests : IClassFixture<BaGetWebApplicationFactory>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly HttpClient _client;

        public ApiIntegrationTests(BaGetWebApplicationFactory factory, ITestOutputHelper output)
        {
            _factory = factory.WithOutput(output);
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task IndexReturnsOk()
        {
            using var response = await _client.GetAsync("v3/index.json");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(TestData.ServiceIndex, content);
        }

        [Fact]
        public async Task SearchReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            using var response = await _client.GetAsync("v3/search");
            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration""
  },
  ""totalHits"": 1,
  ""data"": [
    {
      ""id"": ""DefaultPackage"",
      ""version"": ""1.2.3"",
      ""description"": ""Default package description"",
      ""authors"": [
        ""Default package author""
      ],
      ""iconUrl"": """",
      ""licenseUrl"": """",
      ""projectUrl"": """",
      ""registration"": ""http://localhost/v3/registration/defaultpackage/index.json"",
      ""summary"": """",
      ""tags"": [],
      ""title"": """",
      ""totalDownloads"": 0,
      ""versions"": [
        {
          ""@id"": ""http://localhost/v3/registration/defaultpackage/1.2.3.json"",
          ""version"": ""1.2.3"",
          ""downloads"": 0
        }
      ]
    }
  ]
}", json);
        }

        [Fact]
        public async Task SearchReturnsEmpty()
        {
            using var response = await _client.GetAsync("v3/search?q=PackageDoesNotExist");
            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration""
  },
  ""totalHits"": 0,
  ""data"": []
}", json);
        }

        [Fact]
        public async Task AutocompleteReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            using var response = await _client.GetAsync("v3/autocomplete");
            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 1,
  ""data"": [
    ""DefaultPackage""
  ]
}", json);
        }

        [Fact]
        public async Task AutocompleteReturnsEmpty()
        {
            using var response = await _client.GetAsync("v3/autocomplete?q=PackageDoesNotExist");
            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 0,
  ""data"": []
}", json);
        }

        [Fact]
        public async Task AutocompleteVersionsReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            using var response = await _client.GetAsync("v3/autocomplete?id=DefaultPackage");
            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 1,
  ""data"": [
    ""1.2.3""
  ]
}", json);
        }

        [Fact]
        public async Task AutocompleteVersionsReturnsEmpty()
        {
            using var response = await _client.GetAsync("v3/autocomplete?id=PackageDoesNotExist");
            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 0,
  ""data"": []
}", json);
        }

        [Fact]
        public async Task VersionListReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            var response = await _client.GetAsync("v3/package/DefaultPackage/index.json");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{""versions"":[""1.2.3""]}", content);
        }

        [Fact]
        public async Task VersionListReturnsNotFound()
        {
            using var response = await _client.GetAsync("v3/package/PackageDoesNotExist/index.json");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task PackageDownloadReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            using var response = await _client.GetAsync("v3/package/DefaultPackage/1.2.3/DefaultPackage.1.2.3.nupkg");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task PackageDownloadReturnsNotFound()
        {
            using var response = await _client.GetAsync(
                "v3/package/PackageDoesNotExist/1.0.0/PackageDoesNotExist.1.0.0.nupkg");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task NuspecDownloadReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            using var response = await _client.GetAsync(
                "v3/package/DefaultPackage/1.2.3/DefaultPackage.1.2.3.nuspec");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task NuspecDownloadReturnsNotFound()
        {
            using var response = await _client.GetAsync(
                "v3/package/PackageDoesNotExist/1.0.0/PackageDoesNotExist.1.0.0.nuspec");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task PackageMetadataReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            using var response = await _client.GetAsync("v3/registration/DefaultPackage/index.json");
            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@id"": ""http://localhost/v3/registration/defaultpackage/index.json"",
  ""@type"": [
    ""catalog:CatalogRoot"",
    ""PackageRegistration"",
    ""catalog:Permalink""
  ],
  ""count"": 1,
  ""items"": [
    {
      ""@id"": ""http://localhost/v3/registration/defaultpackage/index.json"",
      ""count"": 1,
      ""lower"": ""1.2.3"",
      ""upper"": ""1.2.3"",
      ""items"": [
        {
          ""@id"": ""http://localhost/v3/registration/defaultpackage/1.2.3.json"",
          ""packageContent"": ""http://localhost/v3/package/defaultpackage/1.2.3/defaultpackage.1.2.3.nupkg"",
          ""catalogEntry"": {
            ""downloads"": 0,
            ""hasReadme"": false,
            ""packageTypes"": [
              ""Dependency""
            ],
            ""releaseNotes"": """",
            ""repositoryUrl"": """",
            ""id"": ""DefaultPackage"",
            ""version"": ""1.2.3"",
            ""authors"": ""Default package author"",
            ""dependencyGroups"": [],
            ""description"": ""Default package description"",
            ""iconUrl"": """",
            ""language"": """",
            ""licenseUrl"": """",
            ""listed"": true,
            ""minClientVersion"": """",
            ""packageContent"": ""http://localhost/v3/package/defaultpackage/1.2.3/defaultpackage.1.2.3.nupkg"",
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
}", json);
        }

        [Fact]
        public async Task PackageMetadataReturnsNotFound()
        {
            using var response = await _client.GetAsync("v3/registration/PackageDoesNotExist/index.json");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task PackageMetadataLeafReturnsOk()
        {
            await _factory.AddPackageAsync(PackageData.Default);

            using var response = await _client.GetAsync("v3/registration/DefaultPackage/1.2.3.json");
            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@id"": ""http://localhost/v3/registration/defaultpackage/1.2.3.json"",
  ""@type"": [
    ""Package"",
    ""http://schema.nuget.org/catalog#Permalink""
  ],
  ""listed"": true,
  ""packageContent"": ""http://localhost/v3/package/defaultpackage/1.2.3/defaultpackage.1.2.3.nupkg"",
  ""published"": ""2020-01-01T00:00:00Z"",
  ""registration"": ""http://localhost/v3/registration/defaultpackage/index.json""
}", json);
        }

        [Fact]
        public async Task PackageMetadataLeafReturnsNotFound()
        {
            using var response = await _client.GetAsync("v3/registration/PackageDoesNotExist/1.0.0.json");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task PackageDependentsReturnsOk()
        {
            using var response = await _client.GetAsync("v3/dependents?packageId=DefaultPackage");

            var content = await response.Content.ReadAsStreamAsync();
            var json = PrettifyJson(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""totalHits"": 0,
  ""data"": []
}", json);
        }

        [Fact]
        public async Task PackageDependentsReturnsBadRequest()
        {
            using var response = await _client.GetAsync("v3/dependents");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        private string PrettifyJson(Stream jsonStream)
        {
            using var writer = new StringWriter();
            using var jsonWriter = new JsonTextWriter(writer)
            {
                Formatting = Formatting.Indented,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };

            using var reader = new StreamReader(jsonStream);
            using var jsonReader = new JsonTextReader(reader);

            jsonWriter.WriteToken(jsonReader);
            return writer.ToString();
        }
    }
}
