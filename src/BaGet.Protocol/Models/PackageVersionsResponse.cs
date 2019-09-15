using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The full list of versions for a single package.
    ///
    /// See <see href="https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#enumerate-package-versions"/>.
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
