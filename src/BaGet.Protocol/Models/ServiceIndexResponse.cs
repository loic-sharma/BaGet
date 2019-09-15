using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The entry point for a NuGet package source used by the client to find APIs.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/overview
    /// NuGet.org: https://api.nuget.org/v3-index/index.json
    /// </summary>
    public class ServiceIndexResponse
    {
        /// <summary>
        /// The service index's version.
        /// </summary>
        [JsonProperty("version")]
        public string Version { get; set; }

        /// <summary>
        /// The resources declared by this service index.
        /// </summary>
        [JsonProperty("resources")]
        public IReadOnlyList<ServiceIndexItem> Resources { get; set; }
    }
}
