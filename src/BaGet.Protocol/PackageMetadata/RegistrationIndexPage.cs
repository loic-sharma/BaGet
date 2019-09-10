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
        [JsonProperty(PropertyName = "@id")]
        public string PageUrl { get; set; }

        public int Count { get; set; }

        /// <summary>
        /// Null if this package's registration is paged. The items can be found
        /// by following the page's <see cref="PageUrl"/>.
        /// </summary>
        [JsonProperty(PropertyName = "items")]
        public IReadOnlyList<RegistrationIndexPageItem> ItemsOrNull { get; set; }

        /// <summary>
        /// This page's lowest package version.
        /// </summary>
        [JsonConverter(typeof(NuGetVersionConverter))]
        public NuGetVersion Lower { get; set; }

        /// <summary>
        /// This page's highest package version.
        /// </summary>
        [JsonConverter(typeof(NuGetVersionConverter))]
        public NuGetVersion Upper { get; set; }
    }
}
