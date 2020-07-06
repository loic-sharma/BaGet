using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The full list of versions for a single package.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#enumerate-package-versions
    /// </summary>
    public class PackageVersionsResponse
    {
        /// <summary>
        /// The versions, lowercased and normalized.
        /// </summary>
        [JsonPropertyName("versions")]
        public IReadOnlyList<string> Versions { get; set; }
    }
}
