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
                Startup.RegistrationIndexRouteName,
                new { Id = id.ToLowerInvariant() });

        public static string PackageRegistrationLeaf(this IUrlHelper url, string id, NuGetVersion version)
            => url.RouteUrl(
                Startup.RegistrationLeafRouteName,
                new {
                    Id = id.ToLowerInvariant(),
                    Version = version.ToNormalizedString().ToLowerInvariant()
                });

        public static string PackageDownload(this IUrlHelper url, string id, NuGetVersion version)
        {
            id = id.ToLowerInvariant();
            var versionString = version.ToNormalizedString().ToLowerInvariant();

            return url.RouteUrl(
                Startup.PackageDownloadRouteName,
                new
                {
                    id,
                    Version = versionString,
                    IdVersion = $"{id}.{versionString}"
                });
        }
    }
}
