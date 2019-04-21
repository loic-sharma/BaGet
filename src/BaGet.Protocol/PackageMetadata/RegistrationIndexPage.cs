using System;
using System.Collections.Generic;
using BaGet.Protocol.Converters;
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
        public RegistrationIndexPage(
            string pageUrl,
            int count,
            IReadOnlyList<RegistrationIndexPageItem> itemsOrNull,
            NuGetVersion lower,
            NuGetVersion upper)
        {
            if (string.IsNullOrEmpty(pageUrl)) throw new ArgumentNullException(nameof(pageUrl));

            PageUrl = pageUrl;
            Count = count;
            ItemsOrNull = itemsOrNull;
            Lower = lower ?? throw new ArgumentNullException(nameof(lower));
            Upper = upper ?? throw new ArgumentNullException(nameof(upper));
        }

        [JsonProperty(PropertyName = "@id")]
        public string PageUrl { get; }

        public int Count { get; }

        /// <summary>
        /// Null if this package's registration is paged. The items can be found
        /// by following the page's <see cref="PageUrl"/>.
        /// </summary>
        [JsonProperty(PropertyName = "items")]
        public IReadOnlyList<RegistrationIndexPageItem> ItemsOrNull { get; }

        /// <summary>
        /// This page's lowest package version.
        /// </summary>
        [JsonConverter(typeof(NuGetVersionConverter))]
        public NuGetVersion Lower { get; }

        /// <summary>
        /// This page's highest package version.
        /// </summary>
        [JsonConverter(typeof(NuGetVersionConverter))]
        public NuGetVersion Upper { get; }
    }
}
