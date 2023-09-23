using System.ComponentModel.DataAnnotations;
using BaGetter.Core;

namespace BaGetter.Gcp
{
    public class GoogleCloudStorageOptions : StorageOptions
    {
        [Required]
        public string BucketName { get; set; }
    }
}
