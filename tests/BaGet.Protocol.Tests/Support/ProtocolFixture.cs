using System.Net;
using System.Net.Http;
using BaGet.Protocol.Internal;

namespace BaGet.Protocol.Tests
{
    public class ProtocolFixture
    {
        public ProtocolFixture()
        {
            var httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

            ServiceIndexClient = new ServiceIndexClient(httpClient, "https://api.nuget.org/v3/index.json");
            ContentClient = new PackageContentClient(httpClient, "https://api.nuget.org/v3-flatcontainer");
            MetadataClient = new PackageMetadataClient(httpClient, "https://api.nuget.org/v3/registration3-gz-semver2");
            SearchClient = new SearchClient(
                httpClient,
                "https://azuresearch-usnc.nuget.org/query",
                "https://azuresearch-ussc.nuget.org/autocomplete");
        }

        public ServiceIndexClient ServiceIndexClient { get; }
        public PackageContentClient ContentClient { get; }
        public PackageMetadataClient MetadataClient { get; }
        public SearchClient SearchClient { get; }
    }
}
