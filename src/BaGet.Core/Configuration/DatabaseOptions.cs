using System.ComponentModel.DataAnnotations;

namespace BaGet.Core
{
    public class DatabaseOptions
    {
        public string Type { get; set; }

        [Required]
        public string ConnectionString { get; set; }
    }
}
