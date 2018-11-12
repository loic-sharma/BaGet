using System.Collections.Generic;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public class PackageVersions
    {
        [JsonConverter(typeof(NuGetVersionListConverter))]
        public IReadOnlyList<NuGetVersion> Versions { get; set; }
    }
}
