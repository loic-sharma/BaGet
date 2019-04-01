using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    /// <summary>
    /// The metadata for a package and all of its versions.
    /// </summary>
    public class RegistrationIndex
    {
        public static readonly IReadOnlyList<string> DefaultType = new List<string>
        {
            "catalog:CatalogRoot",
            "PackageRegistration",
            "catalog:Permalink"
        };

        public RegistrationIndex(
            int count,
            long totalDownloads,
            IReadOnlyList<RegistrationIndexPage> pages,
            IReadOnlyList<string> type = null)
        {
            Count = count;
            Pages = pages ?? throw new ArgumentNullException(nameof(pages));
            Type = type;
        }

        [JsonProperty(PropertyName = "@type")]
        public IReadOnlyList<string> Type { get; }

        /// <summary>
        /// The number of registration pages. See <see cref="Pages"/>. 
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// How many times all versions of this package have been downloaded.
        /// </summary>
        public long TotalDownloads { get; }

        /// <summary>
        /// The pages that contain all of the versions of the package, ordered
        /// by the package's version.
        /// </summary>
        [JsonProperty(PropertyName = "items")]
        public IReadOnlyList<RegistrationIndexPage> Pages { get; }
    }
}
