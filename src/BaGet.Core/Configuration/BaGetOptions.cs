using System;

namespace BaGet.Core.Configuration
{
    public class BaGetOptions
    {
        public Uri PackageSource { get; set; }
        public int PackageDownloadTimeoutSeconds { get; set; }

        public DatabaseOptions Database { get; set; }
        public StorageOptions Storage { get; set; }
        public SearchOptions Search { get; set; }
    }
}
