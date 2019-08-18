using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// The metadata for a single version of a package.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    public class RegistrationLeafResponse
    {
        public static readonly IReadOnlyList<string> DefaultType = new List<string>
        {
            "Package",
            "http://schema.nuget.org/catalog#Permalink"
        };

        public RegistrationLeafResponse(
            string registrationUri,
            bool listed,
            string packageContentUrl,
            DateTimeOffset published,
            string registrationIndexUrl,
            IReadOnlyList<string> type = null)
        {
            RegistrationUri = registrationUri ?? throw new ArgumentNullException(nameof(registrationIndexUrl));
            Listed = listed;
            Published = published;
            PackageContentUrl = packageContentUrl ?? throw new ArgumentNullException(nameof(packageContentUrl));
            RegistrationIndexUrl = registrationIndexUrl ?? throw new ArgumentNullException(nameof(registrationIndexUrl));
            Type = type;
        }

        [JsonProperty(PropertyName = "@id")]
        public string RegistrationUri { get; }

        [JsonProperty(PropertyName = "@type")]
        public IReadOnlyList<string> Type { get; }

        public bool Listed { get; }

        [JsonProperty(PropertyName = "packageContent")]
        public string PackageContentUrl { get; }

        public DateTimeOffset Published { get; }

        [JsonProperty(PropertyName = "registration")]
        public string RegistrationIndexUrl { get; }
    }
}
