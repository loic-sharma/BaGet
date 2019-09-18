using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// The client used to search for packages.
    /// 
    /// See https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
    /// </summary>
    public class RawSearchClient : ISearchClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _searchUrl;
        private readonly string _autocompleteUrl;

        /// <summary>
        /// Create a new Search client.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="searchUrl">The NuGet server's search URL.</param>
        /// <param name="autocompleteUrl">The NuGet server's autocomplete URL.</param>
        public RawSearchClient(HttpClient httpClient, string searchUrl, string autocompleteUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _searchUrl = searchUrl ?? throw new ArgumentNullException(nameof(searchUrl));
            _autocompleteUrl = autocompleteUrl ?? throw new ArgumentNullException(nameof(autocompleteUrl));
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(
            string query = null,
            AutocompleteType type = AutocompleteType.PackageIds,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            var param = (type == AutocompleteType.PackageIds) ? "q" : "id";
            var url = AddSearchQueryString(_autocompleteUrl, query, skip, take, includePrerelease, includeSemVer2, param);

            var response = await _httpClient.DeserializeUrlAsync<AutocompleteResponse>(url, cancellationToken);

            return response.GetResultOrThrow();
        }

        public async Task<SearchResponse> SearchAsync(
            string query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            var url = AddSearchQueryString(_searchUrl, query, skip, take, includePrerelease, includeSemVer2, "q");

            var response = await _httpClient.DeserializeUrlAsync<SearchResponse>(url, cancellationToken);

            return response.GetResultOrThrow();
        }

        private string AddSearchQueryString(
            string uri,
            string query,
            int skip,
            int take,
            bool includePrerelease,
            bool includeSemVer2,
            string queryParamName)
        {
            var queryString = new Dictionary<string, string>();

            if (skip != 0) queryString["skip"] = skip.ToString();
            if (take != 0) queryString["take"] = take.ToString();
            if (includePrerelease) queryString["prerelease"] = true.ToString();
            if (includeSemVer2) queryString["semVerLevel"] = "2.0.0";

            if (!string.IsNullOrEmpty(query))
            {
                queryString[queryParamName] = query;
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
