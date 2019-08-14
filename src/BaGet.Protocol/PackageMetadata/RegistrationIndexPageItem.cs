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
        public RegistrationIndexPageItem(string leafUrl, PackageMetadata packageMetadata, string packageContent)
        {
            if (string.IsNullOrEmpty(leafUrl)) throw new ArgumentNullException(nameof(leafUrl));

            LeafUrl = leafUrl;
            PackageMetadata = packageMetadata ?? throw new ArgumentNullException(nameof(packageMetadata));
            PackageContent = packageContent ?? throw new ArgumentNullException(nameof(packageContent));
        }

        [JsonProperty(PropertyName = "@id")]
        public string LeafUrl { get; }

        [JsonProperty(PropertyName = "catalogEntry")]
        public PackageMetadata PackageMetadata { get; }

        public string PackageContent { get; }
    }
}
