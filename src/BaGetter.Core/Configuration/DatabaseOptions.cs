using System.ComponentModel.DataAnnotations;

namespace BaGetter.Core
{
    public class DatabaseOptions
    {
        public string Type { get; set; }

        [Required]
        public string ConnectionString { get; set; }
    }
}
