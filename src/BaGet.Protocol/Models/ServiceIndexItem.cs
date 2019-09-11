using System;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// A resource in the <see cref="ServiceIndexResponse"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/service-index#resources
    /// </summary>
    public class ServiceIndexItem
    {
        /// <summary>
        /// The resource's base URL.
        /// </summary>
        [JsonProperty("@id")]
        public string ResourceUrl { get; set; }

        /// <summary>
        /// The resource's type.
        /// </summary>
        [JsonProperty("@type")]
        public string Type { get; set; }

        /// <summary>
        /// Human readable comments about the resource.
        /// </summary>
        [JsonProperty("comment")]
        public string Comment { get; set; }
    }
}
