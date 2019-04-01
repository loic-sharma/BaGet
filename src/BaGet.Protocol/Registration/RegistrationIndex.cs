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
        private static readonly IReadOnlyList<string> CachedType = new List<string>
        {
            "catalog:CatalogRoot",
            "PackageRegistration",
            "catalog:Permalink"
        };

        public RegistrationIndex(int count, long totalDownloads, IReadOnlyList<RegistrationIndexPage> pages)
        {
            Count = count;
            Pages = pages ?? throw new ArgumentNullException(nameof(pages));
        }

        [JsonProperty(PropertyName = "@type")]
        public IReadOnlyList<string> Type => CachedType;

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
