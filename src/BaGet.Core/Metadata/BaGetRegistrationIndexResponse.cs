using System.Collections.Generic;
using BaGet.Protocol;
using BaGet.Protocol.Models;

namespace BaGet.Core.Metadata
{
    /// <summary>
    /// BaGet's extensions to a search request. These additions
    /// are not part of the official protocol.
    /// </summary>
    public class BaGetRegistrationIndexResponse : RegistrationIndexResponse
    {
        /// <summary>
        /// How many times all versions of this package have been downloaded.
        /// </summary>
        public long TotalDownloads { get; set; }
    }
}
