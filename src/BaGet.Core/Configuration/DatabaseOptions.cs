namespace BaGet.Core.Configuration
{
    public class DatabaseOptions
    {
        public DatabaseType Type { get; set; }
        public string ConnectionString { get; set; }
    }

    public enum DatabaseType
    {
        Sqlite,
        SqlServer,
    }
}
