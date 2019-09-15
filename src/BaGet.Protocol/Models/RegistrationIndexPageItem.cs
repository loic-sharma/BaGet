using Newtonsoft.Json;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// An item in the <see cref="CatalogIndex"/> that references a <see cref="CatalogLeaf"/>.
    ///
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-leaf-object-in-a-page
    /// </summary>
    public class RegistrationIndexPageItem
    {
        /// <summary>
        /// The URL to the registration leaf.
        /// </summary>
        [JsonProperty("@id")]
        public string RegistrationLeafUrl { get; set; }

        /// <summary>
        /// The catalog entry containing the package metadata.
        /// </summary>
        [JsonProperty("catalogEntry")]
        public PackageMetadata PackageMetadata { get; set; }

        /// <summary>
        /// The URL to the package content (.nupkg)
        /// </summary>
        [JsonProperty("packageContent")]
        public string PackageContentUrl { get; set; }
    }
}
