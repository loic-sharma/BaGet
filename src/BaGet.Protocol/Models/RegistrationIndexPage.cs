using System;
using System.Collections.Generic;
using BaGet.Protocol.Internal;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The registration page object found in the registration index.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page-object
    /// </summary>
    public class RegistrationIndexPage
    {
        [JsonProperty("@id")]
        public string RegistrationPageUrl { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>
        /// Null if this package's registration is paged. The items can be found
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
