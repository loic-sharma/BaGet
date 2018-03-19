using System;

namespace BaGet
{
    public class Options
    {
        public Uri PackageSource { get; set; }
        public int PackageDownloadTimeoutSeconds { get; set; }
        public string PackageStore { get; set; }
        public DatabaseOptions Database { get; set; }
    }

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
