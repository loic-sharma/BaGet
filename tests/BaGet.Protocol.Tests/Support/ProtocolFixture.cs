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
            ContentClient = new PackageContentClient(httpClient, TestData.PackageContentUrl);
            MetadataClient = new PackageMetadataClient(httpClient, TestData.PackageMetadataUrl);
            CatalogClient = new CatalogClient(httpClient, TestData.CatalogIndexUrl);
            SearchClient = new SearchClient(
                httpClient,
                TestData.SearchUrl,
                TestData.AutocompleteUrl);
        }

        public NuGetClient NuGetClient { get; }
        public NuGetClientFactory NuGetClientFactory { get; }

        public ServiceIndexClient ServiceIndexClient { get; }
        public PackageContentClient ContentClient { get; }
        public PackageMetadataClient MetadataClient { get; }
        public SearchClient SearchClient { get; }
        public CatalogClient CatalogClient { get; }
    }
}
