using System;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGet.Extensions
{
    public static class UrlExtensions
    {
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
