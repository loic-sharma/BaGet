using System;
using System.Collections.Generic;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BaGet.Controllers.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific package.
    /// </summary>
    public class RegistrationIndexController
    {
        private readonly BaGetContext _context;

        public RegistrationIndexController(BaGetContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // GET v3/registration/{id}.json
        [HttpGet]
        public object Get(string id)
        {
            // Documentation: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource

            // TODO: Paging of registration items.
            // "Un-paged" example: https://api.nuget.org/v3/registration3/newtonsoft.json/index.json
            // Paged example: https://api.nuget.org/v3/registration3/fake/index.json
            return new
            {
                Count = 1,
                Items = new[]
                {
                    new RegistrationIndexItem(
                        packageId: "Newtonsoft.Json",
                        items: new List<RegistrationIndexLeaf>
                        {
                            new RegistrationIndexLeaf(
                                packageId: "Newtonsoft.Json",
                                catalogEntry: new CatalogEntry(
                                                catalogUri: new Uri("https://api.nuget.org/v3/catalog0/data/2015.02.01.06.24.15/newtonsoft.json.3.5.8.json"),
                                                packageId: "Newtonsoft.Json",
                                                version: "3.5.8"),
                                packageContent: new Uri("https://api.nuget.org/v3-flatcontainer/newtonsoft.json/3.5.8/newtonsoft.json.3.5.8.nupkg")),

                            new RegistrationIndexLeaf(
                                packageId: "Newtonsoft.Json",
                                catalogEntry: new CatalogEntry(
                                                catalogUri: new Uri("https://api.nuget.org/v3/catalog0/data/2016.06.27.12.35.49/newtonsoft.json.9.0.1.json"),
                                                packageId: "Newtonsoft.Json",
                                                version: "9.0.1"),
                                packageContent: new Uri("https://api.nuget.org/v3-flatcontainer/newtonsoft.json/9.0.1/newtonsoft.json.9.0.1.nupkg")),
                        },
                        lower: "3.5.8",
                        upper: "9.0.1"
                    ),
                }
            };
        }

        private class RegistrationIndexItem
        {
            public RegistrationIndexItem(
                string packageId,
                IReadOnlyList<RegistrationIndexLeaf> items,
                string lower,
                string upper)
            {
                if (string.IsNullOrEmpty(packageId)) throw new ArgumentNullException(nameof(packageId));
                if (string.IsNullOrEmpty(lower)) throw new ArgumentNullException(nameof(lower));
                if (string.IsNullOrEmpty(upper)) throw new ArgumentNullException(nameof(upper));

                PackageId = packageId;
                Items = items ?? throw new ArgumentNullException(nameof(items));
                Lower = lower;
                Upper = upper;
            }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public int Count => Items.Count;

            public IReadOnlyList<RegistrationIndexLeaf> Items { get; }

            public string Lower { get; }
            public string Upper { get; }
        }

        private class RegistrationIndexLeaf
        {
            public RegistrationIndexLeaf(string packageId, CatalogEntry catalogEntry, Uri packageContent)
            {
                if (string.IsNullOrEmpty(packageId)) throw new ArgumentNullException(nameof(packageId));

                PackageId = packageId;
                CatalogEntry = catalogEntry ?? throw new ArgumentNullException(nameof(catalogEntry));
                PackageContent = packageContent ?? throw new ArgumentNullException(nameof(packageContent));
            }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public CatalogEntry CatalogEntry { get; }

            public Uri PackageContent { get; }
        }

        private class CatalogEntry
        {
            public CatalogEntry(Uri catalogUri, string packageId, string version)
            {
                if (string.IsNullOrEmpty(nameof(packageId))) throw new ArgumentNullException(nameof(packageId));
                if (string.IsNullOrEmpty(nameof(version))) throw new ArgumentNullException(nameof(version));

                CatalogUri = catalogUri ?? throw new ArgumentNullException(nameof(catalogUri));
                PackageId = packageId;
                Version = version;
            }

            [JsonProperty(PropertyName = "@id")]
            public Uri CatalogUri { get; }

            [JsonProperty(PropertyName = "id")]
            public string PackageId { get; }

            public string Version { get; }
        }
    }
}