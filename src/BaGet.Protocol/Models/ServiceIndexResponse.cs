using System;
using System.Collections.Generic;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
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
        [JsonConverter(typeof(NuGetVersionConverter))]
        public NuGetVersion Version { get; set; }

        /// <summary>
        /// The resources declared by this service index.
        /// </summary>
        [JsonProperty("resources")]
        public IReadOnlyList<ServiceIndexItem> Resources { get; set; }
    }
}
