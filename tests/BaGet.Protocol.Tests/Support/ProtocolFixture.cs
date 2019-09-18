using System.Net;
using System.Net.Http;
using BaGet.Protocol.Internal;

namespace BaGet.Protocol.Tests
{
    public class ProtocolFixture
    {
        public ProtocolFixture()
        {
            var httpClient = new HttpClient(new TestDataHttpMessageHandler());

            NuGetClientFactory = new NuGetClientFactory(httpClient, TestData.ServiceIndexUrl);
            NuGetClient = new NuGetClient(NuGetClientFactory);

            ServiceIndexClient = new ServiceIndexClient(httpClient, TestData.ServiceIndexUrl);
            ContentClient = new RawPackageContentClient(httpClient, TestData.PackageContentUrl);
            MetadataClient = new RawPackageMetadataClient(httpClient, TestData.PackageMetadataUrl);
            CatalogClient = new RawCatalogClient(httpClient, TestData.CatalogIndexUrl);
            SearchClient = new RawSearchClient(
                httpClient,
                TestData.SearchUrl,
                TestData.AutocompleteUrl);
        }

        public NuGetClient NuGetClient { get; }
        public NuGetClientFactory NuGetClientFactory { get; }

        public ServiceIndexClient ServiceIndexClient { get; }
        public RawPackageContentClient ContentClient { get; }
        public RawPackageMetadataClient MetadataClient { get; }
        public RawSearchClient SearchClient { get; }
        public RawCatalogClient CatalogClient { get; }
    }
}
