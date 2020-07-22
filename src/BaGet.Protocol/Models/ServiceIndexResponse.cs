using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The entry point for a NuGet package source used by the client to discover NuGet APIs.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/overview
    /// </summary>
    public class ServiceIndexResponse
    {
        /// <summary>
        /// The service index's version.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// The resources declared by this service index.
        /// </summary>
        [JsonPropertyName("resources")]
        public IReadOnlyList<ServiceIndexItem> Resources { get; set; }
    }
}
