using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Controllers.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific package.
    /// </summary>
    public class RegistrationIndexController : Controller
    {
        private readonly IPackageService _packages;

        public RegistrationIndexController(IPackageService packages)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        }

        // GET v3/registration/{id}.json
        [HttpGet]
        public async Task<object> Get(string id)
        {
            // Documentation: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
            var packages = await _packages.FindAsync(id);
            var versions = packages.Select(p => NuGetVersion.Parse(p.Version)).ToList();

            // TODO: Paging of registration items.
            // "Un-paged" example: https://api.nuget.org/v3/registration3/newtonsoft.json/index.json
            // Paged example: https://api.nuget.org/v3/registration3/fake/index.json
            return new
            {
                Count = packages.Count,
                Items = new[]
                {
                    new RegistrationIndexItem(
                        packageId: id,
                        items: packages.Select(p => new RegistrationIndexLeaf(
                                                        packageId: p.Id,
                                                        catalogEntry: new CatalogEntry(
                                                                            catalogUri: new Uri($"https://api.nuget.org/v3/catalog0/data/2015.02.01.06.24.15/{p.Id}.{p.Version}.json"),
                                                                            packageId: p.Id,
                                                                            version: p.Version),
                                                        packageContent: new Uri($"https://api.nuget.org/v3-flatcontainer/{p.Id}/{p.Version}/{p.Id}.{p.Version}.nupkg")))
                                       .ToList(),
                        lower: versions.Min().ToNormalizedString(),
                        upper: versions.Max().ToNormalizedString()
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