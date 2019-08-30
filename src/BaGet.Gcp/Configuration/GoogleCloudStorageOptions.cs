using System.ComponentModel.DataAnnotations;
using BaGet.Core.Configuration;

namespace BaGet.Gcp.Configuration
{
    public class GoogleCloudStorageOptions : StorageOptions
    {
        [Required]
        public string BucketName { get; set; }
    }
}
