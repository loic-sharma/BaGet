using System.ComponentModel.DataAnnotations;

namespace BaGet.Azure.Configuration
{
    public class BlobStorageOptions
    {
        [Required]
        public string AccountName { get; set; }
        
        [Required]
        public string AccessKey { get; set; }

        [Required]
        public string Container { get; set; }
    }
}
