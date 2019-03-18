using System.ComponentModel.DataAnnotations;

namespace BaGet.Core.Configuration
{
    public class DatabaseOptions
    {
        public DatabaseType Type { get; set; }

        [Required]
        public string ConnectionString { get; set; }
    }

    public enum DatabaseType
    {
        MySql,
        Sqlite,
        SqlServer,
        PostgreSql
    }
}
