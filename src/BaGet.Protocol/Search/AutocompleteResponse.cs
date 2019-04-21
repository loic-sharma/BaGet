using System;
using System.Collections.Generic;

namespace BaGet.Protocol
{
    /// <summary>
    /// The package ids that matched the autocomplete query.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#search-for-package-ids
    /// </summary>
    public class AutocompleteResponse
    {
        public AutocompleteResponse(
            long totalHits,
            IReadOnlyList<string> data,
            AutocompleteContext context = null)
        {
            TotalHits = totalHits;
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Context = context;
        }

        public AutocompleteContext Context { get; }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        public long TotalHits { get; }

        /// <summary>
        /// The package IDs matched by the autocomplete query.
        /// </summary>
        public IReadOnlyList<string> Data { get; }
    }
}
