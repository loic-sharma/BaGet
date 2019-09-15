using BaGet.Protocol.Internal;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// Represents a package dependency.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-dependency
    /// </summary>
    public class DependencyItem
    {
        /// <summary>
        /// The ID of the package dependency.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// The allowed version range of the dependency.
        /// </summary>
        [JsonProperty("range")]
        [JsonConverter(typeof(PackageDependencyRangeConverter))]
        public string Range { get; set; }
    }
}
