﻿using System;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// The metadata for a single version of a package.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    public class RegistrationLeaf
    {
        public RegistrationLeaf(
            string registrationUri,
            bool listed,
            long downloads,
            string packageContentUrl,
            DateTimeOffset published,
            string registrationIndexUrl)
        {
            RegistrationUri = registrationUri ?? throw new ArgumentNullException(nameof(registrationIndexUrl));
            Listed = listed;
            Published = published;
            Downloads = downloads;
            PackageContentUrl = packageContentUrl ?? throw new ArgumentNullException(nameof(packageContentUrl));
            RegistrationIndexUrl = registrationIndexUrl ?? throw new ArgumentNullException(nameof(registrationIndexUrl));
        }

        [JsonProperty(PropertyName = "@id")]
        public string RegistrationUri { get; }

        public bool Listed { get; }

        public long Downloads { get; }

        [JsonProperty(PropertyName = "packageContent")]
        public string PackageContentUrl { get; }

        public DateTimeOffset Published { get; }

        [JsonProperty(PropertyName = "registration")]
        public string RegistrationIndexUrl { get; }
    }
}
