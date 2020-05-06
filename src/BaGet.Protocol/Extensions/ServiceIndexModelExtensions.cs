using System;
using System.Linq;
using BaGet.Protocol.Models;

namespace BaGet.Protocol
{
    /// <summary>
    /// These are documented interpretations of values returned by the Service Index resource.
    /// </summary>
    public static class ServiceIndexModelExtensions
    {
        // See: https://github.com/NuGet/NuGet.Client/blob/e08358296db5bfa6f7f32d6f4ec8de288f3b0388/src/NuGet.Core/NuGet.Protocol/ServiceTypes.cs
        private static readonly string Version200 = "/2.0.0";
        private static readonly string Version300beta = "/3.0.0-beta";
        private static readonly string Version300 = "/3.0.0";
        private static readonly string Version340 = "/3.4.0";
        private static readonly string Version360 = "/3.6.0";
        private static readonly string Version470 = "/4.7.0";
        private static readonly string Version490 = "/4.9.0";
        private static readonly string Version500 = "/5.0.0";
        private static readonly string Version510 = "/5.1.0";

        private static readonly string[] Catalog = { "Catalog" + Version300 };
        private static readonly string[] SearchQueryService = { "SearchQueryService" + Version340, "SearchQueryService" + Version300beta, "SearchQueryService" };
        private static readonly string[] RegistrationsBaseUrl = { "RegistrationsBaseUrl" + Version360, "RegistrationsBaseUrl" + Version340, "RegistrationsBaseUrl" + Version300beta, "RegistrationsBaseUrl" };
        private static readonly string[] SearchAutocompleteService = { "SearchAutocompleteService", "SearchAutocompleteService" + Version300beta };
        private static readonly string[] ReportAbuse = { "ReportAbuseUriTemplate", "ReportAbuseUriTemplate" + Version300 };
        private static readonly string[] PackageDetailsUriTemplate = { "PackageDetailsUriTemplate" + Version510 };
        private static readonly string[] LegacyGallery = { "LegacyGallery" + Version200 };
        private static readonly string[] PackagePublish = { "PackagePublish" + Version200 };
        private static readonly string[] PackageBaseAddress = { "PackageBaseAddress" + Version300 };
        private static readonly string[] RepositorySignatures = { "RepositorySignatures" + Version500, "RepositorySignatures" + Version490, "RepositorySignatures" + Version470 };
        private static readonly string[] SymbolPackagePublish = { "SymbolPackagePublish" + Version490 };

        public static string GetPackageContentResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetRequiredResourceUrl(PackageBaseAddress, nameof(PackageBaseAddress));
        }

        public static string GetPackageMetadataResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetRequiredResourceUrl(RegistrationsBaseUrl, nameof(RegistrationsBaseUrl));
        }

        public static string GetSearchQueryResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetRequiredResourceUrl(SearchQueryService, nameof(SearchQueryService));
        }

        public static string GetCatalogResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetResourceUrl(Catalog);
        }

        public static string GetSearchAutocompleteResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetResourceUrl(SearchAutocompleteService);
        }

        public static string GetResourceUrl(this ServiceIndexResponse serviceIndex, string[] types)
        {
            var resource = types.SelectMany(t => serviceIndex.Resources.Where(r => r.Type == t)).FirstOrDefault();

            return resource?.ResourceUrl.Trim('/');
        }

        public static string GetRequiredResourceUrl(this ServiceIndexResponse serviceIndex, string[] types, string resourceName)
        {
            // For more information on required resources,
            // see: https://docs.microsoft.com/en-us/nuget/api/overview#resources-and-schema
            var resourceUrl = serviceIndex.GetResourceUrl(types);
            if (string.IsNullOrEmpty(resourceUrl))
            {
                throw new InvalidOperationException(
                    $"The service index does not have a resource named '{resourceName}'");
            }

            return resourceUrl;
        }
    }
}
