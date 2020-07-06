using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// The alternate package that should be used instead of a deprecated package.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-deprecation
    /// </summary>
    public class AlternatePackage
    {
        [JsonPropertyName("@id")]
        public string Url { get; set; }

        [JsonPropertyName("@type")]
        public string Type { get; set; }

        /// <summary>
        /// The ID of the alternate package.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The allowed version range, or * if any version is allowed.
        /// </summary>
        [JsonPropertyName("range")]
        public string Range { get; set; }
    }
}
