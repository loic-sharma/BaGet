using System;
using System.Collections.Generic;

namespace BaGet.Protocol
{
    /// <summary>
    /// The package ids that depend on the queried package.
    /// This is an unofficial API that isn't documented.
    /// </summary>
    public class DependentResult
    {
        public DependentResult(int totalHits, IReadOnlyList<string> data)
        {
            TotalHits = totalHits;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>
        /// The total number of matches, disregarding skip and take.
        /// </summary>
        public int TotalHits;

        /// <summary>
        /// The package IDs matched by the dependent query.
        /// </summary>
        public IReadOnlyList<string> Data;
    }
}
