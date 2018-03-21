using System;
using BaGet.Azure.Configuration;

namespace BaGet.Configuration
{
    public class BaGetOptions
    {
        public Uri PackageSource { get; set; }
        public int PackageDownloadTimeoutSeconds { get; set; }

        public DatabaseOptions Database { get; set; }
        public StorageOptions Storage { get; set; }
        public AzureOptions Azure { get; set; }
    }
}
