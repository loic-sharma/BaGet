using System.ComponentModel.DataAnnotations;

namespace BaGet.Azure
{
    public class AzureTableOptions
    {
        [Required]
        public string ConnectionString { get; set; }
    }
}
