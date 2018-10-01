namespace BaGet.Core.Configuration
{
    public class DatabaseOptions
    {
        public bool RunMigrations { get; set; } 
        public DatabaseType Type { get; set; }
        public string ConnectionString { get; set; }
    }

    public enum DatabaseType
    {
        Sqlite,
        SqlServer,
    }
}
