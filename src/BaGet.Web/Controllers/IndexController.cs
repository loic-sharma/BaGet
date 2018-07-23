using System;
using BaGet.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BaGet.Web.Controllers
{
    /// <summary>
    /// The NuGet Service Index. This aids NuGet client to discover this server's services.
    /// </summary>
    public class IndexController : Controller
    {
        // GET v3/index
        [HttpGet]
        public object Get()
        {
            // Documentation: https://docs.microsoft.com/en-us/nuget/api/overview
            // NuGet.org: https://api.nuget.org/v3-index/index.json

            // TODO: Client is picky about versions' formatting. Needs more research. See:
            // https://github.com/NuGet/NuGet.Client/blob/67d34ee8158597377fb26e06987ad6ad40a1b09b/src/NuGet.Core/NuGet.Protocol/Constants.cs
            return new
            {
                Version = "3.0.0-beta.1",
                Resources = new[]
                {
                    // Required
                    new ServiceResource("PackagePublish/Versioned", Url.PackagePublish()),
                    new ServiceResource("SearchQueryService/Versioned", Url.PackageSearch()),
                    new ServiceResource("RegistrationsBaseUrl/Versioned", Url.RegistrationsBase()),
                    new ServiceResource("PackageBaseAddress/Versioned", Url.PackageBase()),

                    // Optional
                    new ServiceResource("SearchAutocompleteService/Versioned", Url.PackageAutocomplete()),
                    //new ServiceResource("ReportAbuseUriTemplate/3.0.0-rc", new Uri("https://google.com")),
                    //new ServiceResource("Catalog/3.0.0", new Uri("https://google.com"))
                }
            };
        }

        private class ServiceResource
        {
            public ServiceResource(string type, string id, string comment = null)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
                Type = type ?? throw new ArgumentNullException(nameof(type));
                Comment = comment ?? string.Empty;
            }

            [JsonProperty(PropertyName = "@id")]
            public string Id { get; }

            [JsonProperty(PropertyName = "@type")]
            public string Type { get; }

            public string Comment { get; }
        }
    }
}