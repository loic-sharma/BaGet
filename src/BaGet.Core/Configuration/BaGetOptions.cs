using System;

namespace BaGet.Core.Configuration
{
    public class BaGetOptions
    {
        /// <summary>
        /// The SHA-256 hash of the API Key required to authenticate package
        /// operations. If empty, package operations do not require authentication.
        /// </summary>
        public string ApiKeyHash { get; set; }

        public Uri PackageSource { get; set; }
        public int PackageDownloadTimeoutSeconds { get; set; }

        public DatabaseOptions Database { get; set; }
        public StorageOptions Storage { get; set; }
        public SearchOptions Search { get; set; }
    }
}
