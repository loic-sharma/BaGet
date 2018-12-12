using System.ComponentModel.DataAnnotations;

namespace BaGet.Core.Configuration
{
    public class BaGetOptions
    {
         /// <summary>
        /// The API Key required to authenticate package
        /// operations. If empty, package operations do not require authentication.
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// The application root URL for usage in reverse proxy scenarios.
        /// </summary>
        public string PathBase { get; set; }

        /// <summary>
        /// How BaGet should interpret package deletion requests.
        /// </summary>
        public PackageDeletionBehavior PackageDeletionBehavior { get; set; } = PackageDeletionBehavior.HardDelete;

        [Required]
        public DatabaseOptions Database { get; set; }

        [Required]
        public StorageOptions Storage { get; set; }

        [Required]
        public SearchOptions Search { get; set; }

        [Required]
        public MirrorOptions Mirror { get; set; }
    }
}
