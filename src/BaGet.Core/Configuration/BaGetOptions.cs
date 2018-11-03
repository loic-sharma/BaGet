namespace BaGet.Core.Configuration
{
    public class BaGetOptions
    {
        /// <summary>
        /// The SHA-256 hash of the API Key required to authenticate package
        /// operations. If empty, package operations do not require authentication.
        /// </summary>
        public string ApiKeyHash { get; set; }

        /// <summary>
        /// If enabled, the database will be updated at app startup by running
        /// Entity Framework migrations. This is not recommended in production.
        /// </summary>
        public bool RunMigrationsAtStartup { get; set; } = true;

        public DatabaseOptions Database { get; set; }
        public StorageOptions Storage { get; set; }
        public SearchOptions Search { get; set; }

        public MirrorOptions Mirror { get; set; }
    }
}
