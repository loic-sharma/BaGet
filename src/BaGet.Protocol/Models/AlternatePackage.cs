using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The alternate package that should be used instead of a deprecated package.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-deprecation
    /// </summary>
    public class AlternatePackage
    {
        [JsonProperty("@id")]
        public string Url { get; set; }

        [JsonProperty("@type")]
        public string Type { get; set; }

        /// <summary>
        /// The ID of the alternate package.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// The allowed version range, or * if any version is allowed.
        /// </summary>
        [JsonProperty("range")]
        public string Range { get; set; }
    }
}
