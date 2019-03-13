using BaGet.Core.Configuration;

namespace BaGet.GCP.Configuration
{
    public class GoogleCloudStorageOptions : StorageOptions
    {
        public string BucketName { get; set; }
    }
}
