using System.ComponentModel.DataAnnotations;
using BaGet.Core.Configuration;

namespace BaGet.GCP.Configuration
{
    public class GoogleCloudStorageOptions : StorageOptions
    {
        [Required]
        public string BucketName { get; set; }
    }
}
