using System.ComponentModel.DataAnnotations;

namespace BaGet.Core.Configuration
{
    public class DatabaseOptions
    {
        public DatabaseType Type { get; set; }
        public SqlDialect SqlDialect { get; set; } = SqlDialect.Default;

        [Required]
        public string ConnectionString { get; set; }

        /// <summary>
        /// If enabled, the database will be updated at app startup by running
        /// Entity Framework migrations. This is not recommended in production.
        /// </summary>
        public bool RunMigrationsAtStartup { get; set; } = true;
    }
}
