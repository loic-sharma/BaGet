using System.ComponentModel.DataAnnotations;

namespace BaGet.Azure.Configuration
{
    public class AzureSearchOptions : Core.Configuration.SearchOptions
    {
        [Required]
        public string AccountName { get; set; }

        [Required]
        public string ApiKey { get; set; }
    }
}
