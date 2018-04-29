using System;

namespace BaGet.Core.Configuration
{
    public class MirrorOptions
    {
        /// <summary>
        /// The v3 index that will be mirrored.
        /// </summary>
        public Uri PackageSource { get; set; }

        /// <summary>
        /// The time before a download from the package source times out.
        /// </summary>
        public int PackageDownloadTimeoutSeconds { get; set; }

        /// <summary>
        /// If true, packages that aren't found locally will be indexed
        /// using the upstream source.
        /// </summary>
        public bool EnableReadThroughCaching { get; set; }
    }
}
