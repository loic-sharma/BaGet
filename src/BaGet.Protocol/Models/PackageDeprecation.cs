using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// A package's metadata.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-deprecation
    /// </summary>
    public class PackageDeprecation
    {
        /// <summary>
        /// The URL to the document used to produce this object.
        /// </summary>
        [JsonProperty("@id")]
        public string CatalogLeafUrl { get; set; }

        /// <summary>
        /// The reasons why the package was deprecated.
        /// Deprecation reasons include: "Legacy", "CriticalBugs", and "Other".
        /// </summary>
        [JsonProperty("reasons")]
        public IReadOnlyList<string> Reasons { get; set; }

        /// <summary>
        /// The additional details about this deprecation.
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// The alternate package that should be used instead.
        /// </summary>
        [JsonProperty("alternatePackage")]
        public AlternatePackage AlternatePackage { get; set; }
    }
}
