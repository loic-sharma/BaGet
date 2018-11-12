using System;
using System.Linq;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public class ServiceIndexService : IServiceIndexService
    {
        // See: https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Protocol/Constants.cs
        public static readonly string Version200 = "/2.0.0";
        public static readonly string Version300beta = "/3.0.0-beta";
        public static readonly string Version300 = "/3.0.0";
        public static readonly string Version340 = "/3.4.0";
        public static readonly string Versioned = "/Versioned";
        public static readonly string Version470 = "/4.7.0";
        public static readonly string Version490 = "/4.9.0";

        public static readonly string[] SearchQueryService = { "SearchQueryService" + Versioned, "SearchQueryService" + Version340, "SearchQueryService" + Version300beta };
        public static readonly string[] RegistrationsBaseUrl = { "RegistrationsBaseUrl" + Versioned, "RegistrationsBaseUrl" + Version340, "RegistrationsBaseUrl" + Version300beta };
        public static readonly string[] SearchAutocompleteService = { "SearchAutocompleteService" + Versioned, "SearchAutocompleteService" + Version300beta };
        public static readonly string[] ReportAbuse = { "ReportAbuseUriTemplate" + Versioned, "ReportAbuseUriTemplate" + Version300 };
        public static readonly string[] LegacyGallery = { "LegacyGallery" + Versioned, "LegacyGallery" + Version200 };
        public static readonly string[] PackagePublish = { "PackagePublish" + Versioned, "PackagePublish" + Version200 };
        public static readonly string[] PackageBaseAddress = { "PackageBaseAddress" + Versioned, "PackageBaseAddress" + Version300 };
        public static readonly string[] RepositorySignatures = { "RepositorySignatures" + Version490, "RepositorySignatures" + Version470 };
        public static readonly string[] SymbolPackagePublish = { "SymbolPackagePublish" + Version490 };

        private readonly IServiceIndexClient _client;
        private readonly Lazy<Task<ServiceIndex>> _serviceIndexTask;

        public ServiceIndexService(
            string serviceIndexUrl,
            IServiceIndexClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _serviceIndexTask = new Lazy<Task<ServiceIndex>>(() =>
            {
                return client.GetServiceIndexAsync(serviceIndexUrl);
            });
        }

        public async Task<string> GetPackageContentUrlAsync()
        {
            return await GetUrlForResourceType(PackageBaseAddress);
        }

        public async Task<string> GetRegistrationUrlAsync()
        {
            return await GetUrlForResourceType(RegistrationsBaseUrl);
        }

        public async Task<string> GetSearchUrlAsync()
        {
            return await GetUrlForResourceType(SearchQueryService);
        }

        private async Task<string> GetUrlForResourceType(string[] types)
        {
            var serviceIndex = await _serviceIndexTask.Value;
            var resource = serviceIndex.Resources.First(r => types.Contains(r.Type));

            return resource.Url.Trim('/');
        }
    }
}
