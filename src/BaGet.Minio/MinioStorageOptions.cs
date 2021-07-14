using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Minio
{
    public class MinioStorageOptions
    {
        /// <summary>
        /// Gets or sets the user ID that identifies the storage service account
        /// </summary>
        [RequiredIf(nameof(SecretKey), null, IsInverted = true)]
        public string AccessKey { get; set; }

        /// <summary>
        /// Gets or sets the password to the storage service account
        /// </summary>
        [RequiredIf(nameof(AccessKey), null, IsInverted = true)]
        public string SecretKey { get; set; }

        /// <summary>
        /// Gets or sets the URL of the object storage service
        /// </summary>
        [Required]
        public string Endpoint { get; set; }

        /// <summary>
        /// Gets or sets the name of the storage bucket
        /// </summary>
        [Required]
        public string Bucket { get; set; }

        /// <summary>
        /// Gets or sets a prefix to all objects stored on the service
        /// </summary>
        public string Prefix { get; set; }

        /// <summary>
        /// Gets or sets an optional region for the storage provider
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// Gets or sets whether HTTPS support is enabled
        /// </summary>
        public bool Secure { get; set; }

    }
}
