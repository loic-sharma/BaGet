using System;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BaGet.Controllers
{
    /// <summary>
    /// The NuGet Service Index. This aids NuGet client to discover this server's services.
    /// </summary>
    public class IndexController
    {
        // GET v3/index
        [HttpGet]
        public object Get()
        {
            // Documentation: https://docs.microsoft.com/en-us/nuget/api/overview
            // NuGet.org: https://api.nuget.org/v3-index/index.json
            return new
            {
                Version = "3.0.0-beta.1",
                Resources = new[]
                {
                    // Required
                    new ServiceResource("PackagePublish/2.0.0", new Uri("https://google.com")),
                    new ServiceResource("SearchQueryService/3.0.0-rc", new Uri("https://google.com")),
                    new ServiceResource("RegistrationsBaseUrl/3.0.0-rc", new Uri("https://google.com")),
                    new ServiceResource("PackageBaseAddress/3.0.0", new Uri("https://google.com")),

                    // Optional
                    //new ServiceResource("SearchAutocompleteService/3.0.0-rc", new Uri("https://google.com")),
                    //new ServiceResource("ReportAbuseUriTemplate/3.0.0-rc", new Uri("https://google.com")),
                    //new ServiceResource("Catalog/3.0.0", new Uri("https://google.com"))
                }
            };
        }

        private class ServiceResource
        {
            public ServiceResource(string type, Uri id, string comment = null)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
                Type = type ?? throw new ArgumentNullException(nameof(type));
                Comment = comment ?? string.Empty;
            }

            [JsonProperty(PropertyName = "@id")]
            public Uri Id { get; }

            [JsonProperty(PropertyName = "@type")]
            public string Type { get; }

            public string Comment { get; }
        }
    }
}