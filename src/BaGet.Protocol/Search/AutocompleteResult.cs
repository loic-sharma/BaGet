using System;
using System.Collections.Generic;

namespace BaGet.Protocol
{
    /// <summary>
    /// The package ids that matched the autocomplete query.
    /// Documentation: https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource#search-for-package-ids
    /// </summary>
    public class AutocompleteResult
    {
        public AutocompleteResult(int totalHits, IReadOnlyList<string> data)
        {
            TotalHits = totalHits;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        public int TotalHits;

        /// <summary>
        /// The package IDs matched by the autocomplete query.
        /// </summary>
        public IReadOnlyList<string> Data;
    }
}
