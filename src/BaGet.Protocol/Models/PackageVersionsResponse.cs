using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// The full list of versions for a single package.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#enumerate-package-versions
    /// Example: https://api.nuget.org/v3-flatcontainer/newtonsoft.json/index.json
    /// </summary>
    public class PackageVersionsResponse
    {
        /// <summary>
        /// The versions, lowercased and normalized.
        /// </summary>
        [JsonProperty("versions")]
        public IReadOnlyList<string> Versions { get; set; }
    }
}
