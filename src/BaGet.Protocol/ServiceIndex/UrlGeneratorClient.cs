using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public class UrlGeneratorClient : IUrlGenerator
    {
        private readonly ServiceIndexResponse _serviceIndex;

        // See: https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Protocol/Constants.cs
        private static readonly string Version200 = "/2.0.0";
        private static readonly string Version300beta = "/3.0.0-beta";
        private static readonly string Version300 = "/3.0.0";
        private static readonly string Version340 = "/3.4.0";
        private static readonly string Version360 = "/3.6.0";
        private static readonly string Versioned = "/Versioned";
        private static readonly string Version470 = "/4.7.0";
        private static readonly string Version490 = "/4.9.0";

        private static readonly string[] SearchQueryService = { "SearchQueryService" + Versioned, "SearchQueryService" + Version340, "SearchQueryService" + Version300beta };
        private static readonly string[] RegistrationsBaseUrl = { "RegistrationsBaseUrl" + Versioned, "RegistrationsBaseUrl" + Version360, "RegistrationsBaseUrl" + Version340, "RegistrationsBaseUrl" + Version300beta };
        private static readonly string[] SearchAutocompleteService = { "SearchAutocompleteService" + Versioned, "SearchAutocompleteService" + Version300beta };
        private static readonly string[] ReportAbuse = { "ReportAbuseUriTemplate" + Versioned, "ReportAbuseUriTemplate" + Version300 };
        private static readonly string[] LegacyGallery = { "LegacyGallery" + Versioned, "LegacyGallery" + Version200 };
        private static readonly string[] PackagePublish = { "PackagePublish" + Versioned, "PackagePublish" + Version200 };
        private static readonly string[] PackageBaseAddress = { "PackageBaseAddress" + Versioned, "PackageBaseAddress" + Version300 };
        private static readonly string[] RepositorySignatures = { "RepositorySignatures" + Version490, "RepositorySignatures" + Version470 };
        private static readonly string[] SymbolPackagePublish = { "SymbolPackagePublish" + Version490 };

        private UrlGeneratorClient(ServiceIndexResponse serviceIndex)
        {
            _serviceIndex = serviceIndex ?? throw new ArgumentNullException(nameof(serviceIndex));
        }

        public static async Task<UrlGeneratorClient> CreateAsync(IServiceIndexResource serviceIndex, CancellationToken cancellationToken = default)
        {
            return new UrlGeneratorClient(await serviceIndex.GetAsync(cancellationToken));
        }

        public string GetPackageContentResourceUrl()
        {
            return GetUrlForResourceTypes(PackageBaseAddress);
        }

        public string GetPackageMetadataResourceUrl()
        {
            return GetUrlForResourceTypes(RegistrationsBaseUrl);
        }

        public string GetPackagePublishResourceUrl()
        {
            return GetUrlForResourceTypes(PackagePublish);
        }

        public string GetSymbolPublishResourceUrl()
        {
            return GetUrlForResourceTypes(SymbolPackagePublish);
        }

        public string GetSearchResourceUrl()
        {
            return GetUrlForResourceTypes(SearchQueryService);
        }

        public string GetAutocompleteResourceUrl()
        {
            return GetUrlForResourceTypes(SearchAutocompleteService);
        }

        public string GetRegistrationIndexUrl(string id)
        {
            var packageMetadataUrl = GetPackageMetadataResourceUrl();
            var packageId = id.ToLowerInvariant();

            return $"{packageMetadataUrl}/{packageId}/index.json";
        }

        public string GetRegistrationPageUrl(string id, NuGetVersion lower, NuGetVersion upper)
        {
            var packageMetadataUrl = GetPackageMetadataResourceUrl();
            var packageId = id.ToLowerInvariant();
            var lowerVersion = lower.ToNormalizedString().ToLowerInvariant();
            var upperVersion = upper.ToNormalizedString().ToLowerInvariant();

            return $"{packageMetadataUrl}/{packageId}/page/{lowerVersion}/{upperVersion}.json";
        }

        public string GetRegistrationLeafUrl(string id, NuGetVersion version)
        {
            var packageMetadataUrl = GetPackageMetadataResourceUrl();
            var packageId = id.ToLowerInvariant();
            var packageVersion = version.ToNormalizedString().ToLowerInvariant();

            return $"{packageMetadataUrl}/{packageId}/{packageVersion}.json";
        }

        public string GetPackageVersionsUrl(string id)
        {
            var packageContentUrl = GetPackageContentResourceUrl();
            var packageId = id.ToLowerInvariant();

            return $"{packageContentUrl}/{packageId}/index.json";
        }

        public string GetPackageDownloadUrl(string id, NuGetVersion version)
        {
            var packageContentUrl = GetPackageContentResourceUrl();
            var packageId = id.ToLowerInvariant();
            var packageVersion = version.ToNormalizedString().ToLowerInvariant();

            return $"{packageContentUrl}/{packageId}/{packageVersion}/{packageId}.{packageVersion}.nupkg";
        }

        public string GetPackageManifestDownloadUrl(string id, NuGetVersion version)
        {
            var packageContentUrl = GetPackageContentResourceUrl();
            var packageId = id.ToLowerInvariant();
            var packageVersion = version.ToNormalizedString().ToLowerInvariant();

            return $"{packageContentUrl}/{packageId}/{packageVersion}/{packageId}.nuspec";
        }

        private string GetUrlForResourceTypes(string[] types)
        {
            var resource = types.SelectMany(t => _serviceIndex.Resources.Where(r => r.Type == t)).FirstOrDefault();

            return resource?.Url.Trim('/');
        }
    }

    public class UrlGeneratorClientFactory : IUrlGeneratorFactory
    {
        private readonly Lazy<Task<UrlGeneratorClient>> _urlTask;

        public UrlGeneratorClientFactory(IServiceIndexResource serviceIndex)
        {
            _urlTask = new Lazy<Task<UrlGeneratorClient>>(async () =>
            {
                return await UrlGeneratorClient.CreateAsync(serviceIndex);
            });
        }

        public async Task<IUrlGenerator> CreateAsync()
        {
            return await _urlTask.Value;
        }
    }
}
