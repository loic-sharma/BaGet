using System;

namespace BaGet
{
    public class Options
    {
        public Uri PackageSource { get; set; }
        public int PackageDownloadTimeoutSeconds { get; set; }
        public string PackageStore { get; set; }
    }
}
