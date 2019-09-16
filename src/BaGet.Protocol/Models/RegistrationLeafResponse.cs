using System;
using System.Collections.Generic;
using Newtonsoft.Json;

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
        [JsonProperty("@id")]
        public string RegistrationLeafUrl { get; set; }

        /// <summary>
        /// The registration leaf's type.
        /// </summary>
        [JsonProperty("@type")]
        public IReadOnlyList<string> Type { get; set; }

        /// <summary>
        /// Whether the package is listed.
        /// </summary>
        [JsonProperty("listed")]
        public bool Listed { get; set; }

        /// <summary>
        /// The URL to the package content (.nupkg).
        /// </summary>
        [JsonProperty("packageContent")]
        public string PackageContentUrl { get; set; }

        /// <summary>
        /// The date the package was published. On NuGet.org, <see cref="Published"/>
        /// is set to the year 1900 if the package is unlisted.
        /// </summary>
        [JsonProperty("published")]
        public DateTimeOffset Published { get; set; }

        /// <summary>
        /// The URL to the package's registration index.
        /// </summary>
        [JsonProperty("registration")]
        public string RegistrationIndexUrl { get; set; }
    }
}
