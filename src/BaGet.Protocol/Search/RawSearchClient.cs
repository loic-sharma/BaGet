using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
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

        /// <summary>
        /// Create a new Search client.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="searchUrl">The NuGet server's search URL.</param>
        public RawSearchClient(HttpClient httpClient, string searchUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _searchUrl = searchUrl ?? throw new ArgumentNullException(nameof(searchUrl));
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

            return await _httpClient.GetFromJsonAsync<SearchResponse>(url, cancellationToken);
        }

        internal static string AddSearchQueryString(
            string uri,
            string query,
            int? skip,
            int? take,
            bool includePrerelease,
            bool includeSemVer2,
            string queryParamName)
        {
            var queryString = new Dictionary<string, string>();

            if (skip.HasValue && skip.Value > 0) queryString["skip"] = skip.ToString();
            if (take.HasValue) queryString["take"] = take.ToString();
            if (includePrerelease) queryString["prerelease"] = true.ToString();
            if (includeSemVer2) queryString["semVerLevel"] = "2.0.0";

            if (!string.IsNullOrEmpty(query))
            {
                queryString[queryParamName] = query;
            }

            return AddQueryString(uri, queryString);
        }

        // See: https://github.com/aspnet/AspNetCore/blob/8c02467b4a218df3b1b0a69bceb50f5b64f482b1/src/Http/WebUtilities/src/QueryHelpers.cs#L63
        private static string AddQueryString(string uri, Dictionary<string, string> queryString)
        {
            if (uri.IndexOf('#') != -1) throw new InvalidOperationException("URL anchors are not supported");
            if (uri.IndexOf('?') != -1) throw new InvalidOperationException("Adding query strings to URL with query strings is not supported");

            var builder = new StringBuilder(uri);
            var hasQuery = false;

            foreach (var parameter in queryString)
            {
                builder.Append(hasQuery ? '&' : '?');
                builder.Append(Uri.EscapeDataString(parameter.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(parameter.Value));
                hasQuery = true;
            }

            return builder.ToString();
        }
    }
}
