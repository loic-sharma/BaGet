using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The metadata for a single version of a package.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    public class RegistrationLeafResponse
    {
        public static readonly IReadOnlyList<string> DefaultType = new List<string>
        {
            "Package",
            "http://schema.nuget.org/catalog#Permalink"
        };

        [JsonProperty("@id")]
        public string RegistrationLeafUrl { get; set; }

        [JsonProperty("@type")]
        public IReadOnlyList<string> Type { get; set; }

        [JsonProperty("listed")]
        public bool Listed { get; set; }

        [JsonProperty("packageContent")]
        public string PackageContentUrl { get; set; }

        [JsonProperty("published")]
        public DateTimeOffset Published { get; set; }

        [JsonProperty("registration")]
        public string RegistrationIndexUrl { get; set; }
    }
}
