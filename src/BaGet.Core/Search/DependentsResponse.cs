using System;
using System.Collections.Generic;

namespace BaGet.Core.Search
{
    /// <summary>
    /// The package ids that depend on the queried package.
    /// This is an unofficial API that isn't part of the NuGet protocol.
    /// </summary>
    public class DependentsResponse
    {
        public DependentsResponse(long totalHits, IReadOnlyList<string> data)
        {
            TotalHits = totalHits;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        public long TotalHits { get; }

        /// <summary>
        /// The package IDs matched by the dependent query.
        /// </summary>
        public IReadOnlyList<string> Data { get; }
    }
}
