using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public class SearchClient : ISearchClient
    {
        private readonly HttpClient _httpClient;

        public SearchClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<SearchResponse> GetSearchResultsAsync(string searchUrl)
        {
            var response = await _httpClient.DeserializeUrlAsync<SearchResponse>(searchUrl);

            return response.GetResultOrThrow();
        }

        public async Task<AutocompleteResult> GetAutocompleteResultsAsync(string searchUrl)
        {
            var response = await _httpClient.DeserializeUrlAsync<AutocompleteResult>(searchUrl);

            return response.GetResultOrThrow();
        }
    }
}
