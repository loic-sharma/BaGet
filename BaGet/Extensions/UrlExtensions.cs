using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGet.Extensions
{
    public static class UrlExtensions
    {
        public static string PackagePublish(this IUrlHelper url) => url.RouteUrl(Startup.UploadRouteName);
        public static string PackageSearch(this IUrlHelper url) => url.RouteUrl(Startup.SearchRouteName);
        public static string PackageAutocomplete(this IUrlHelper url) => url.RouteUrl(Startup.AutocompleteRouteName);

        public static string PackageRegistrationIndex(this IUrlHelper url, string id)
            => url.RouteUrl(
                Startup.RegistrationLeafRouteName,
                new { id });

        public static string PackageRegistrationLeaf(this IUrlHelper url, string id, NuGetVersion version)
            => url.RouteUrl(
                Startup.RegistrationLeafRouteName,
                new { id, version = version.ToNormalizedString() });

        public static string PackageDownload(this IUrlHelper url, string id, NuGetVersion version)
            => url.RouteUrl(
                Startup.PackageDownloadRouteName,
                new
                {
                    id,
                    version = version.ToNormalizedString(),
                    idVersion = $"{id}.{version.ToNormalizedString()}"
                });
    }
}
