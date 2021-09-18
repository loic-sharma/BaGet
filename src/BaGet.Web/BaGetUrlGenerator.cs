using System;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NuGet.Versioning;

namespace BaGet.Web
{
    // TODO: This should validate the "Host" header against known valid values
    public class BaGetUrlGenerator : IUrlGenerator
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly LinkGenerator _linkGenerator;

        public BaGetUrlGenerator(IHttpContextAccessor httpContextAccessor, LinkGenerator linkGenerator)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _linkGenerator = linkGenerator ?? throw new ArgumentNullException(nameof(linkGenerator));
        }

        public string GetServiceIndexUrl()
        {
            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.IndexRouteName,
                values: null);
        }

        public string GetPackageContentResourceUrl()
        {
            return AbsoluteUrl("v3/package");
        }

        public string GetPackageMetadataResourceUrl()
        {
            return AbsoluteUrl("v3/registration");
        }

        public string GetPackagePublishResourceUrl()
        {
            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.UploadPackageRouteName,
                values: null);
        }

        public string GetSymbolPublishResourceUrl()
        {
            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.UploadSymbolRouteName,
                values: null);
        }

        public string GetSearchResourceUrl()
        {
            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.SearchRouteName,
                values: null);
        }

        public string GetAutocompleteResourceUrl()
        {
            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.AutocompleteRouteName,
                values: null);
        }

        public string GetRegistrationIndexUrl(string id)
        {
            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.RegistrationIndexRouteName,
                values: new { Id = id.ToLowerInvariant() });
        }

        public string GetRegistrationPageUrl(string id, NuGetVersion lower, NuGetVersion upper)
        {
            // BaGet does not support paging the registration resource.
            throw new NotImplementedException();
        }

        public string GetRegistrationLeafUrl(string id, NuGetVersion version)
        {
            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.RegistrationLeafRouteName,
                values: new
                {
                    Id = id.ToLowerInvariant(),
                    Version = version.ToNormalizedString().ToLowerInvariant(),
                });
        }

        public string GetPackageVersionsUrl(string id)
        {
            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.PackageVersionsRouteName,
                values: new { Id = id.ToLowerInvariant() });
        }

        public string GetPackageDownloadUrl(string id, NuGetVersion version)
        {
            id = id.ToLowerInvariant();
            var versionString = version.ToNormalizedString().ToLowerInvariant();

            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.PackageDownloadRouteName,
                values: new
                {
                    Id = id,
                    Version = versionString,
                    IdVersion = $"{id}.{versionString}"
                });
        }

        public string GetPackageManifestDownloadUrl(string id, NuGetVersion version)
        {
            id = id.ToLowerInvariant();
            var versionString = version.ToNormalizedString().ToLowerInvariant();

            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.PackageDownloadRouteName,
                values: new
                {
                    Id = id,
                    Version = versionString,
                    Id2 = id,
                });
        }

        public string GetPackageIconDownloadUrl(string id, NuGetVersion version)
        {
            id = id.ToLowerInvariant();
            var versionString = version.ToNormalizedString().ToLowerInvariant();

            return _linkGenerator.GetUriByRouteValues(
                _httpContextAccessor.HttpContext,
                Routes.PackageDownloadIconRouteName,
                values: new
                {
                    Id = id,
                    Version = versionString
                });
        }

        private string AbsoluteUrl(string relativePath)
        {
            var request = _httpContextAccessor.HttpContext.Request;

            return string.Concat(
                request.Scheme,
                "://",
                request.Host.ToUriComponent(),
                request.PathBase.ToUriComponent(),
                "/",
                relativePath);
        }
    }
}
