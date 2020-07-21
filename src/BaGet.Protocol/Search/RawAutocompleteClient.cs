using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// The client used to search for packages.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource
    /// </summary>
    public class RawAutocompleteClient : IAutocompleteClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _autocompleteUrl;

        /// <summary>
        /// Create a new Search client.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="autocompleteUrl">The NuGet server's autocomplete URL.</param>
        public RawAutocompleteClient(HttpClient httpClient, string autocompleteUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _autocompleteUrl = autocompleteUrl ?? throw new ArgumentNullException(nameof(autocompleteUrl));
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(
            string query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            var url = RawSearchClient.AddSearchQueryString(
                _autocompleteUrl,
                query,
                skip,
                take,
                includePrerelease,
                includeSemVer2,
                "q");

            return await _httpClient.GetFromJsonAsync<AutocompleteResponse>(url, cancellationToken);
        }

        public async Task<AutocompleteResponse> ListPackageVersionsAsync(
            string packageId,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            var url = RawSearchClient.AddSearchQueryString(
                _autocompleteUrl,
                packageId,
                skip: null,
                take: null,
                includePrerelease,
                includeSemVer2,
                "id");

            return await _httpClient.GetFromJsonAsync<AutocompleteResponse>(url, cancellationToken);
        }
    }
}
