using System.Collections.Generic;
using BaGet.Protocol;

namespace BaGet.Core.Metadata
{
    /// <summary>
    /// BaGet's extensions to a search request. These additions
    /// are not part of the official protocol.
    /// </summary>
    public class BaGetRegistrationIndexResponse : RegistrationIndexResponse
    {
        public BaGetRegistrationIndexResponse(
            int count,
            long totalDownloads,
            IReadOnlyList<RegistrationIndexPage> pages,
            IReadOnlyList<string> type = null)
          : base (count, pages, type)
        {
            TotalDownloads = totalDownloads;
        }

        /// <summary>
        /// How many times all versions of this package have been downloaded.
        /// </summary>
        public long TotalDownloads { get; }
    }
}
