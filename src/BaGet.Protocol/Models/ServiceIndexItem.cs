using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// A resource in the <see cref="ServiceIndexResponse"/>.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/service-index#resources
    /// </summary>
    public class ServiceIndexItem
    {
        /// <summary>
        /// The resource's base URL.
        /// </summary>
        [JsonPropertyName("@id")]
        public string ResourceUrl { get; set; }

        /// <summary>
        /// The resource's type.
        /// </summary>
        [JsonPropertyName("@type")]
        public string Type { get; set; }

        /// <summary>
        /// Human readable comments about the resource.
        /// </summary>
        [JsonPropertyName("comment")]
        public string Comment { get; set; }
    }
}
