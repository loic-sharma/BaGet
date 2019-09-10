using System;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// An item in a registration page.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-leaf-object-in-a-page
    /// </summary>
    public class RegistrationIndexPageItem
    {
        [JsonProperty(PropertyName = "@id")]
        public string LeafUrl { get; set; }

        [JsonProperty(PropertyName = "catalogEntry")]
        public PackageMetadata PackageMetadata { get; set; }

        [JsonProperty(PropertyName = "packageContent")]
        public string PackageContentUrl { get; set; }
    }
}
