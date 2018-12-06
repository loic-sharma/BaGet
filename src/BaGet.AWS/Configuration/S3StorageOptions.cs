using System.ComponentModel.DataAnnotations;
using BaGet.Core.Validation;

namespace BaGet.AWS.Configuration
{
    public class S3StorageOptions
    {
        [RequiredIf(nameof(KeySecret), null, IsInverted = true)]
        public string KeyId { get; set; }

        [RequiredIf(nameof(KeyId), null, IsInverted = true)]
        public string KeySecret { get; set; }

        [Required]
        public string Region { get; set; }

        [Required]
        public string Bucket { get; set; }

        public string Prefix { get; set; }
    }
}
