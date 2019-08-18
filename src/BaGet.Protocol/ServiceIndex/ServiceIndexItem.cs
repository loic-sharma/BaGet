using System;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// A resource in the <see cref="ServiceIndexResponse"/>.
    /// See: https://docs.microsoft.com/en-us/nuget/api/service-index#resources
    /// </summary>
    public class ServiceIndexItem
    {
        public ServiceIndexItem(string type, string url, string comment = null)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Comment = comment ?? string.Empty;
        }

        /// <summary>
        /// The resource's base URL.
        /// </summary>
        [JsonProperty(PropertyName = "@id")]
        public string Url { get; }

        /// <summary>
        /// The resource's type.
        /// </summary>
        [JsonProperty(PropertyName = "@type")]
        public string Type { get; }

        /// <summary>
        /// Human readable comments about the resource.
        /// </summary>
        public string Comment { get; }
    }
}
