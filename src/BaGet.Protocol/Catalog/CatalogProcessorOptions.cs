using System;

namespace BaGet.Protocol.Catalog
{
    /// <summary>
    /// The options to configure <see cref="CatalogProcessor"/>.
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/3a468fe534a03dcced897eb5992209fdd3c4b6c9/src/NuGet.Protocol.Catalog/CatalogProcessorSettings.cs
    /// </summary>
    public class CatalogProcessorOptions
    {
        /// <summary>
        /// The minimum commit timestamp to use when no cursor value has been saved.
        /// </summary>
        public DateTimeOffset? DefaultMinCommitTimestamp { get; set; }

        /// <summary>
        /// The absolute minimum (exclusive) commit timestamp to process in the catalog.
        /// Use this to filter out catalog items that are "too old".
        /// Set this to <see cref="DateTimeOffset.MinValue"/> to process all catalog items.
        /// </summary>
        public DateTimeOffset MinCommitTimestamp { get; set; }

        /// <summary>
        /// The absolute maximum (inclusive) commit timestamp to process in the catalog.
        /// Use this to filter out catalog items that are "too new".
        /// Set this to <see cref="DateTimeOffset.MaxValue"/> to process all catalog items.
        /// </summary>
        public DateTimeOffset MaxCommitTimestamp { get; set; }

        /// <summary>
        /// If multiple catalog leaves are found in a page concerning the same package ID and version, only the latest
        /// is processed.
        /// </summary>
        public bool ExcludeRedundantLeaves { get; set; }
    }
}
