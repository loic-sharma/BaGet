using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
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

        [JsonProperty(PropertyName = "@id")]
        public string RegistrationLeafUrl { get; set; }

        [JsonProperty(PropertyName = "@type")]
        public IReadOnlyList<string> Type { get; set; }

        public bool Listed { get; set; }

        [JsonProperty(PropertyName = "packageContent")]
        public string PackageContentUrl { get; set; }

        public DateTimeOffset Published { get; set; }

        [JsonProperty(PropertyName = "registration")]
        public string RegistrationIndexUrl { get; set; }
    }
}
