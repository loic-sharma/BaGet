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

            ServiceIndexClient = new RawServiceIndexClient(httpClient, TestData.ServiceIndexUrl);
            ContentClient = new RawPackageContentClient(httpClient, TestData.PackageContentUrl);
            MetadataClient = new RawPackageMetadataClient(httpClient, TestData.PackageMetadataUrl);
            SearchClient = new RawSearchClient(httpClient, TestData.SearchUrl);
            AutocompleteClient = new RawAutocompleteClient(httpClient, TestData.AutocompleteUrl);
            CatalogClient = new RawCatalogClient(httpClient, TestData.CatalogIndexUrl);
        }

        public NuGetClient NuGetClient { get; }
        public NuGetClientFactory NuGetClientFactory { get; }

        public RawServiceIndexClient ServiceIndexClient { get; }
        public RawPackageContentClient ContentClient { get; }
        public RawPackageMetadataClient MetadataClient { get; }
        public RawSearchClient SearchClient { get; }
        public RawAutocompleteClient AutocompleteClient { get; }
        public RawCatalogClient CatalogClient { get; }
    }
}
