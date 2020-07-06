using System.Text.Json.Serialization;
using BaGet.Protocol.Internal;

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
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The allowed version range of the dependency.
        /// </summary>
        [JsonPropertyName("range")]
        [JsonConverter(typeof(PackageDependencyRangeJsonConverter))]
        public string Range { get; set; }
    }
}
