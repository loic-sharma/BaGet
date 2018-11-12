using System;
using System.Collections.Generic;

namespace BaGet.Protocol
{
    /// <summary>
    /// The response to a search query.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource#response
    /// </summary>
    public class SearchResponse
    {
        public SearchResponse(int totalHits, IReadOnlyList<SearchResult> data)
        {
            TotalHits = totalHits;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        public int TotalHits;

        /// <summary>
        /// The packages that matched the search query.
        /// </summary>
        public IReadOnlyList<SearchResult> Data;
    }
}
