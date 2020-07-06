using System.Text.Json.Serialization;

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
        [JsonPropertyName("@id")]
        public string RegistrationLeafUrl { get; set; }

        /// <summary>
        /// The catalog entry containing the package metadata.
        /// </summary>
        [JsonPropertyName("catalogEntry")]
        public PackageMetadata PackageMetadata { get; set; }

        /// <summary>
        /// The URL to the package content (.nupkg)
        /// </summary>
        [JsonPropertyName("packageContent")]
        public string PackageContentUrl { get; set; }
    }
}
