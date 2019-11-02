using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Azure
{
    public class AzureSearchOptions : SearchOptions
    {
        [Required]
        public string AccountName { get; set; }

        [Required]
        public string ApiKey { get; set; }
    }
}
