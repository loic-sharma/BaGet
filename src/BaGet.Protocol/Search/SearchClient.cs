using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <summary>
    /// The client to interact with an upstream source's Search resource.
    /// </summary>
    public class SearchClient : ISearchService
    {
        private readonly IUrlGeneratorFactory _urlGenerator;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Create a new Search client.
        /// </summary>
        /// <param name="urlGenerator">The service to generate URLs to upstream resources.</param>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        public SearchClient(IUrlGeneratorFactory urlGenerator, HttpClient httpClient)
        {
            _urlGenerator = urlGenerator ?? throw new ArgumentNullException(nameof(urlGenerator));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <inheritdoc />
        public async Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken = default)
        {
            var urlGenerator = await _urlGenerator.CreateAsync();
            var autocompleteUrl = urlGenerator.GetAutocompleteResourceUrl();
            var param = (request.Type == AutocompleteRequestType.PackageIds) ? "q" : "id";
            var url = AddSearchQueryString(autocompleteUrl, request, param);

            var response = await _httpClient.DeserializeUrlAsync<AutocompleteResponse>(url, cancellationToken);

            return response.GetResultOrThrow();
        }

        /// <inheritdoc />
        public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            var urlGenerator = await _urlGenerator.CreateAsync();
            var autocompleteUrl = urlGenerator.GetSearchResourceUrl();
            var url = AddSearchQueryString(autocompleteUrl, request, "q");

            var response = await _httpClient.DeserializeUrlAsync<SearchResponse>(url, cancellationToken);

            return response.GetResultOrThrow();
        }

        private string AddSearchQueryString(string uri, SearchRequest request, string queryParamName)
        {
            var queryString = new Dictionary<string, string>();

            if (request.Skip != 0) queryString["skip"] = request.Skip.ToString();
            if (request.Take != 0) queryString["take"] = request.Take.ToString();
            if (request.IncludePrerelease) queryString["prerelease"] = true.ToString();
            if (request.IncludeSemVer2) queryString["semVerLevel"] = "2.0.0";

            if (!string.IsNullOrEmpty(request.Query))
            {
                queryString[queryParamName] = request.Query;
            }

            return AddQueryString(uri, queryString);
        }

        // See: https://github.com/aspnet/AspNetCore/blob/8c02467b4a218df3b1b0a69bceb50f5b64f482b1/src/Http/WebUtilities/src/QueryHelpers.cs#L63
        private string AddQueryString(string uri, Dictionary<string, string> queryString)
        {
            if (uri.IndexOf('#') != -1) throw new InvalidOperationException("URL anchors are not supported");
            if (uri.IndexOf('?') != -1) throw new InvalidOperationException("Adding query strings to URL with query strings is not supported");

            var builder = new StringBuilder(uri);
            var hasQuery = false;

            foreach (var parameter in queryString)
            {
                builder.Append(hasQuery ? '&' : '?');
                builder.Append(UrlEncoder.Default.Encode(parameter.Key));
                builder.Append('=');
                builder.Append(UrlEncoder.Default.Encode(parameter.Value));
                hasQuery = true;
            }

            return builder.ToString();
        }
    }
}
