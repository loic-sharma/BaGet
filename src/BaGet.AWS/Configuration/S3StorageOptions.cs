using System.ComponentModel.DataAnnotations;
using BaGet.Core.Validation;

namespace BaGet.AWS.Configuration
{
    public class S3StorageOptions
    {
        [RequiredIf(nameof(SecretKey), null, IsInverted = true)]
        public string AccessKey { get; set; }

        [RequiredIf(nameof(AccessKey), null, IsInverted = true)]
        public string SecretKey { get; set; }

        [RequiredIf(nameof(ServiceUrl), null)]
        public string Region { get; set; }

        [RequiredIf(nameof(Region), null)]
        public string ServiceUrl { get; set; }

        [Required]
        public string Bucket { get; set; }

        public string Prefix { get; set; }
    }
}
