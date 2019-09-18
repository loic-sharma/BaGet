using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Gcp.Configuration
{
    public class GoogleCloudStorageOptions : StorageOptions
    {
        [Required]
        public string BucketName { get; set; }
    }
}
