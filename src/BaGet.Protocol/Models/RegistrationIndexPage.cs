using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The registration page object found in the registration index.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page-object
    /// </summary>
    public class RegistrationIndexPage
    {
        /// <summary>
        /// The URL to the registration page.
        /// </summary>
        [JsonProperty("@id")]
        public string RegistrationPageUrl { get; set; }

        /// <summary>
        /// The number of registration leafs in the page.
        /// </summary>
        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>
        /// <see langword="null"/> if this package's registration is paged. The items can be found
        /// by following the page's <see cref="RegistrationPageUrl"/>.
        /// </summary>
        [JsonProperty("items")]
        public IReadOnlyList<RegistrationIndexPageItem> ItemsOrNull { get; set; }

        /// <summary>
        /// This page's lowest package version. The version should be lowercased, normalized,
        /// and the SemVer 2.0.0 build metadata removed, if any.
        /// </summary>
        [JsonProperty("lower")]
        public string Lower { get; set; }

        /// <summary>
        /// This page's highest package version. The version should be lowercased, normalized,
        /// and the SemVer 2.0.0 build metadata removed, if any.
        /// </summary>
        [JsonProperty("upper")]
        public string Upper { get; set; }
    }
}
