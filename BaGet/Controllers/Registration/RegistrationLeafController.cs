using System;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BaGet.Controllers.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific version of a specific package.
    /// </summary>
    public class RegistrationLeafController
    {
        // GET v3/registration/{id}/{version}.json
        [HttpGet]
        public RegistrationLeaf Get(string id, string version)
        {
            // Documentation: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
            return new RegistrationLeaf(
                registrationUri: new Uri("https://api.nuget.org/v3/registration3/newtonsoft.json/9.0.1.json"),
                listed: true,
                packageContent: new Uri("https://api.nuget.org/v3-flatcontainer/newtonsoft.json/9.0.1/newtonsoft.json.9.0.1.nupkg"),
                published: DateTimeOffset.Now,
                registrationIndexUri: new Uri("https://api.nuget.org/v3/registration3/newtonsoft.json/index.json"));
        }

        public class RegistrationLeaf
        {
            public RegistrationLeaf(
                Uri registrationUri,
                bool listed,
                Uri packageContent,
                DateTimeOffset published,
                Uri registrationIndexUri)
            {
                RegistrationUri = registrationUri ?? throw new ArgumentNullException(nameof(registrationIndexUri));
                Listed = listed;
                Published = published;
                PackageContent = packageContent ?? throw new ArgumentNullException(nameof(packageContent));
                RegistrationIndexUri = registrationIndexUri ?? throw new ArgumentNullException(nameof(registrationIndexUri));
            }

            [JsonProperty(PropertyName = "@id")]
            public Uri RegistrationUri { get; }

            public bool Listed { get; }

            public Uri PackageContent { get; }

            public DateTimeOffset Published { get; }

            [JsonProperty(PropertyName = "registration")]
            public Uri RegistrationIndexUri { get; }
        }
    }
}