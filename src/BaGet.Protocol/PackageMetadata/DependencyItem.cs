using BaGet.Protocol.Internal;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// Represents a package dependency.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-dependency
    /// </summary>
    public class DependencyItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("range")]
        [JsonConverter(typeof(PackageDependencyRangeConverter))]
        public string Range { get; set; }
    }
}
