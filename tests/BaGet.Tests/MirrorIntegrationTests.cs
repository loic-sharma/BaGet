using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    public class MirrorIntegrationTests : IDisposable
    {
        private readonly BaGetApplication _upstream;
        private readonly BaGetApplication _downstream;
        private readonly HttpClient _downstreamClient;
        private readonly Stream _packageStream;

        public MirrorIntegrationTests(ITestOutputHelper output)
        {
            _upstream = new BaGetApplication(output);
            _downstream = new BaGetApplication(output, _upstream.CreateClient());

            _downstreamClient = _downstream.CreateClient();
            _packageStream = TestResources.GetResourceStream(TestResources.Package);
        }

        [Fact]
        public async Task SearchExcludesUpstream()
        {
            await _upstream.AddPackageAsync(_packageStream);

            using var downstreamResponse = await _downstreamClient.GetAsync("v3/search");
            var downstreamContent = await downstreamResponse.Content.ReadAsStreamAsync();
            var downstreamJson = downstreamContent.ToPrettifiedJson();

            // The downstream package source should not have the package.
            Assert.Equal(HttpStatusCode.OK, downstreamResponse.StatusCode);
            Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration""
  },
  ""totalHits"": 0,
  ""data"": []
}", downstreamJson);
        }

        [Fact]
        public async Task VersionListIncludesUpstream()
        {
            await _upstream.AddPackageAsync(_packageStream);

            var response = await _downstreamClient.GetAsync("v3/package/TestData/index.json");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{""versions"":[""1.2.3""]}", content);
        }

        [Fact]
        public async Task PackageDownloadIncludesUpstream()
        {
            await _upstream.AddPackageAsync(_packageStream);

            using var response = await _downstreamClient.GetAsync("v3/package/TestData/1.2.3/TestData.1.2.3.nupkg");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task NuspecDownloadIncludesUpstream()
        {
            await _upstream.AddPackageAsync(_packageStream);

            using var response = await _downstreamClient.GetAsync(
                "v3/package/TestData/1.2.3/TestData.1.2.3.nuspec");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task PackageMetadataIncludesUpstream()
        {
            await _upstream.AddPackageAsync(_packageStream);

            using var response = await _downstreamClient.GetAsync("v3/registration/TestData/index.json");
            var content = await response.Content.ReadAsStreamAsync();
            var json = content.ToPrettifiedJson();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
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
            ""packageTypes"": [],
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
}", json);
        }

        [Fact]
        public async Task PackageMetadataLeafIncludesUpstream()
        {
            await _upstream.AddPackageAsync(_packageStream);

            using var response = await _downstreamClient.GetAsync("v3/registration/TestData/1.2.3.json");
            var content = await response.Content.ReadAsStreamAsync();
            var json = content.ToPrettifiedJson();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(@"{
  ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
  ""@type"": [
    ""Package"",
    ""http://schema.nuget.org/catalog#Permalink""
  ],
  ""listed"": true,
  ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
  ""published"": ""2020-01-01T00:00:00Z"",
  ""registration"": ""http://localhost/v3/registration/testdata/index.json""
}", json);
        }

        public void Dispose()
        {
            _upstream.Dispose();
            _downstream.Dispose();
        }
    }
}
