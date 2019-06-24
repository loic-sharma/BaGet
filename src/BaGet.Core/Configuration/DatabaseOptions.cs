using System.ComponentModel.DataAnnotations;
using BaGet.Core.Validation;

namespace BaGet.Core.Configuration
{
    public class DatabaseOptions
    {
        public DatabaseType Type { get; set; }

        [RequiredIf(nameof(FromEnvironmentVariable), false)]
        public string ConnectionString { get; set; }

        [RequiredIf(nameof(ConnectionString), null)]
        public bool FromEnvironmentVariable { get; set; }
    }

    public enum DatabaseType
    {
        MySql,
        Sqlite,
        SqlServer,
        PostgreSql
    }
}
