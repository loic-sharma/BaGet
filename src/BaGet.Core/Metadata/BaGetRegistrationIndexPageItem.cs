using System.Text.Json.Serialization;
using BaGet.Protocol.Models;

namespace BaGet.Core
{
    /// <summary>
    /// BaGet's extensions to a registration index page.
    /// Extends <see cref="RegistrationIndexPageItem"/>.
    /// </summary>
    /// <remarks>
    /// TODO: After this project is updated to .NET 5, make <see cref="BaGetRegistrationIndexPageItem"/>
    /// extend <see cref="RegistrationIndexPageItem"/> and remove identical properties.
    /// Properties that are modified should be marked with the "new" modified.
    /// See: https://github.com/dotnet/runtime/pull/32107
    /// </remarks>
    public class BaGetRegistrationIndexPageItem
    {
#region Original properties from RegistrationIndexPageItem.
        [JsonPropertyName("@id")]
        public string RegistrationLeafUrl { get; set; }

        [JsonPropertyName("packageContent")]
        public string PackageContentUrl { get; set; }
#endregion

        /// <summary>
        /// The catalog entry containing the package metadata.
        /// This was modified to use BaGet's extended package metadata model.
        /// </summary>
        [JsonPropertyName("catalogEntry")]
        public BaGetPackageMetadata PackageMetadata { get; set; }
    }
}
