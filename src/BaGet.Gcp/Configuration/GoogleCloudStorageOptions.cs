using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Gcp
{
    public class GoogleCloudStorageOptions : StorageOptions
    {
        [Required]
        public string BucketName { get; set; }
    }
}
