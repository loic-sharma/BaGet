using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// An item in a registration page.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-leaf-object-in-a-page.
    /// </summary>
    public class RegistrationIndexPageItem
    {
        [JsonProperty("@id")]
        public string RegistrationLeafUrl { get; set; }

        [JsonProperty("catalogEntry")]
        public PackageMetadata PackageMetadata { get; set; }

        [JsonProperty("packageContent")]
        public string PackageContentUrl { get; set; }
    }
}
