using BaGet.Core.Configuration;

namespace BaGet.GCP.Configuration
{
    public class GoogleBucketStorageOptions : StorageOptions
    {
        public string BucketName { get; set; }
    }
}
