using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Azure.Configuration
{
    public class AzureSearchOptions : SearchOptions
    {
        [Required]
        public string AccountName { get; set; }

        [Required]
        public string ApiKey { get; set; }
    }
}
