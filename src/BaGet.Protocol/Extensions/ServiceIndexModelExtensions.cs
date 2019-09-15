using System.Linq;
using BaGet.Protocol.Models;

namespace BaGet.Protocol
{
    /// <summary>
    /// These are documented interpretations of values returned by the Service Index resource.
    /// </summary>
    public static class ServiceIndexModelExtensions
    {
        // See: https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Protocol/Constants.cs
        private static readonly string Version200 = "/2.0.0";
        private static readonly string Version300beta = "/3.0.0-beta";
        private static readonly string Version300 = "/3.0.0";
        private static readonly string Version340 = "/3.4.0";
        private static readonly string Version360 = "/3.6.0";
        private static readonly string Versioned = "/Versioned";
        private static readonly string Version470 = "/4.7.0";
        private static readonly string Version490 = "/4.9.0";

        private static readonly string[] Catalog = { "Catalog" + Version300 };
        private static readonly string[] SearchQueryService = { "SearchQueryService" + Versioned, "SearchQueryService" + Version340, "SearchQueryService" + Version300beta };
        private static readonly string[] RegistrationsBaseUrl = { "RegistrationsBaseUrl" + Versioned, "RegistrationsBaseUrl" + Version360, "RegistrationsBaseUrl" + Version340, "RegistrationsBaseUrl" + Version300beta };
        private static readonly string[] SearchAutocompleteService = { "SearchAutocompleteService" + Versioned, "SearchAutocompleteService" + Version300beta };
        private static readonly string[] ReportAbuse = { "ReportAbuseUriTemplate" + Versioned, "ReportAbuseUriTemplate" + Version300 };
        private static readonly string[] LegacyGallery = { "LegacyGallery" + Versioned, "LegacyGallery" + Version200 };
        private static readonly string[] PackagePublish = { "PackagePublish" + Versioned, "PackagePublish" + Version200 };
        private static readonly string[] PackageBaseAddress = { "PackageBaseAddress" + Versioned, "PackageBaseAddress" + Version300 };
        private static readonly string[] RepositorySignatures = { "RepositorySignatures" + Version490, "RepositorySignatures" + Version470 };
        private static readonly string[] SymbolPackagePublish = { "SymbolPackagePublish" + Version490 };

        public static string GetPackageContentResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetResourceUrl(PackageBaseAddress);
        }

        public static string GetPackageMetadataResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetResourceUrl(RegistrationsBaseUrl);
        }

        public static string GetCatalogResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetResourceUrl(Catalog);
        }

        public static string GetSearchQueryResourceUrl(this ServiceIndexResponse serviceIndex)
        {
            return serviceIndex.GetResourceUrl(SearchQueryService);
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
    }
}
