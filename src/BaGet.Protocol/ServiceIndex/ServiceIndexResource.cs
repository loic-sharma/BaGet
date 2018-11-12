using System;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    public class ServiceIndexResource
    {
        public ServiceIndexResource(string type, string url, string comment = null)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Comment = comment ?? string.Empty;
        }

        [JsonProperty(PropertyName = "@id")]
        public string Url { get; }

        [JsonProperty(PropertyName = "@type")]
        public string Type { get; }

        public string Comment { get; }
    }
}