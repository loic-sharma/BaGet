using System;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGet.Extensions
{
    public static class UrlExtensions
    {
        public static string PackageBase(this IUrlHelper url) => url.AbsoluteUrl("v3/package");
        public static string RegistrationsBase(this IUrlHelper url) => url.AbsoluteUrl("v3/registration");
        public static string PackagePublish(this IUrlHelper url) => url.AbsoluteRouteUrl(Startup.UploadRouteName);
        public static string PackageSearch(this IUrlHelper url) => url.AbsoluteRouteUrl(Startup.SearchRouteName);
        public static string PackageAutocomplete(this IUrlHelper url) => url.AbsoluteRouteUrl(Startup.AutocompleteRouteName);

        public static string PackageRegistration(this IUrlHelper url, string id)
            => url.AbsoluteRouteUrl(
                Startup.RegistrationIndexRouteName,
                new { Id = id.ToLowerInvariant() });

        public static string PackageRegistration(this IUrlHelper url, string id, NuGetVersion version)
            => url.AbsoluteRouteUrl(
                Startup.RegistrationLeafRouteName,
                new {
                    Id = id.ToLowerInvariant(),
                    Version = version.ToNormalizedString().ToLowerInvariant()
                });

        public static string PackageDownload(this IUrlHelper url, string id, NuGetVersion version)
        {
            id = id.ToLowerInvariant();
            var versionString = version.ToNormalizedString().ToLowerInvariant();

            return url.AbsoluteRouteUrl(
                Startup.PackageDownloadRouteName,
                new
                {
                    id,
                    Version = versionString,
                    IdVersion = $"{id}.{versionString}"
                });
        }

        public static string AbsoluteUrl(this IUrlHelper url, string relativePath)
        {
            var request = url.ActionContext.HttpContext.Request;

            return new Uri(new Uri(request.Scheme + "://" + request.Host.Value), relativePath).ToString();
        }

        public static string AbsoluteRouteUrl(this IUrlHelper url, string routeName, object routeValues = null)
            => url.RouteUrl(routeName, routeValues, url.ActionContext.HttpContext.Request.Scheme);
    }
}
