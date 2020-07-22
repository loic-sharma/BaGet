using System.Collections.Generic;
using System.Text.Json.Serialization;

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
        [JsonPropertyName("@id")]
        public string CatalogLeafUrl { get; set; }

        /// <summary>
        /// The reasons why the package was deprecated.
        /// Deprecation reasons include: "Legacy", "CriticalBugs", and "Other".
        /// </summary>
        [JsonPropertyName("reasons")]
        public IReadOnlyList<string> Reasons { get; set; }

        /// <summary>
        /// The additional details about this deprecation.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>
        /// The alternate package that should be used instead.
        /// </summary>
        [JsonPropertyName("alternatePackage")]
        public AlternatePackage AlternatePackage { get; set; }
    }
}
