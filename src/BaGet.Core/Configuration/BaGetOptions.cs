using System.ComponentModel.DataAnnotations;

namespace BaGet.Core
{
    public class BaGetOptions
    {
        /// <summary>
        /// The API Key required to authenticate package
        /// operations. If empty, package operations do not require authentication.
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// The application root URL for usage in reverse proxy scenarios.
        /// </summary>
        public string PathBase { get; set; }

        /// <summary>
        /// If enabled, the database will be updated at app startup by running
        /// Entity Framework migrations. This is not recommended in production.
        /// </summary>
        public bool RunMigrationsAtStartup { get; set; } = true;

        /// <summary>
        /// How BaGet should interpret package deletion requests.
        /// </summary>
        public PackageDeletionBehavior PackageDeletionBehavior { get; set; } = PackageDeletionBehavior.Unlist;

        /// <summary>
        /// If enabled, pushing a package that already exists will replace the
        /// existing package.
        /// </summary>
        public bool AllowPackageOverwrites { get; set; } = false;

        /// <summary>
        /// If true, disables package pushing, deleting, and re-listing.
        /// </summary>
        public bool IsReadOnlyMode { get; set; } = false;

        /// <summary>
        /// The URLs the BaGet server will use.
        /// As per documentation <a href="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host?view=aspnetcore-3.1#server-urls">here (Server URLs)</a>.
        /// </summary>
        public string Urls { get; set; }

        public DatabaseOptions Database { get; set; }

        public StorageOptions Storage { get; set; }

        public SearchOptions Search { get; set; }

        public MirrorOptions Mirror { get; set; }
    }
}
