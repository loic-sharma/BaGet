using System.Collections.Generic;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    // Documentation: https://docs.microsoft.com/en-us/nuget/api/overview
    // NuGet.org: https://api.nuget.org/v3-index/index.json
    public class ServiceIndex
    {
        [JsonConverter(typeof(NuGetVersionConverter))]
        public NuGetVersion Version { get; set; }

        public IReadOnlyList<ServiceIndexResource> Resources { get; set; }
    }
}
