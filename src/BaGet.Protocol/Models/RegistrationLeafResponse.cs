using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The metadata for a single version of a package.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-leaf
    /// </summary>
    public class RegistrationLeafResponse
    {
        public static readonly IReadOnlyList<string> DefaultType = new List<string>
        {
            "Package",
            "http://schema.nuget.org/catalog#Permalink"
        };

        /// <summary>
        /// The URL to the registration leaf.
        /// </summary>
        [JsonPropertyName("@id")]
        public string RegistrationLeafUrl { get; set; }

        /// <summary>
        /// The registration leaf's type.
        /// </summary>
        [JsonPropertyName("@type")]
        public IReadOnlyList<string> Type { get; set; }

        /// <summary>
        /// Whether the package is listed.
        /// </summary>
        [JsonPropertyName("listed")]
        public bool Listed { get; set; }

        /// <summary>
        /// The URL to the package content (.nupkg).
        /// </summary>
        [JsonPropertyName("packageContent")]
        public string PackageContentUrl { get; set; }

        /// <summary>
        /// The date the package was published. On NuGet.org, <see cref="Published"/>
        /// is set to the year 1900 if the package is unlisted.
        /// </summary>
        [JsonPropertyName("published")]
        public DateTimeOffset Published { get; set; }

        /// <summary>
        /// The URL to the package's registration index.
        /// </summary>
        [JsonPropertyName("registration")]
        public string RegistrationIndexUrl { get; set; }
    }
}
