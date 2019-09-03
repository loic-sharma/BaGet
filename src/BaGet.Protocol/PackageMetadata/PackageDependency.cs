using BaGet.Protocol.Internal;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    public class PackageDependency
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("range")]
        [JsonConverter(typeof(PackageDependencyRangeConverter))]
        public string Range { get; set; }
    }
}
