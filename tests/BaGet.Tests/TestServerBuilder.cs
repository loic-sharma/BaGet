using System;
using System.Collections.Generic;
using System.IO;
using BaGet.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    /// <summary>
    /// fluent builder pattern implementation.
    /// private/hidden Constructor, please use one of the static methods for creation.
    /// </summary>
    public class TestServerBuilder
    {
        private const string DefaultPackagesFolderName = "Packages";

        private readonly string DatabaseTypeKey = $"{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.Type)}";
        private readonly string ConnectionStringKey = $"{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.ConnectionString)}";
        private readonly string StorageTypeKey = $"{nameof(BaGetOptions.Storage)}:{nameof(StorageOptions.Type)}";
        private readonly string FileSystemStoragePathKey = $"{nameof(BaGetOptions.Storage)}:{nameof(FileSystemStorageOptions.Path)}";
        private readonly string SearchTypeKey = $"{nameof(BaGetOptions.Search)}:{nameof(SearchOptions.Type)}";
        private readonly string MirrorEnabledKey = $"{nameof(BaGetOptions.Mirror)}:{nameof(MirrorOptions.Enabled)}";

        private ITestOutputHelper _helper;
        private LogLevel _minimumLevel = LogLevel.None;

        /// <summary>
        /// private/hidden Constructor.
        /// Tests should use some of the static methods!
        /// </summary>
        private TestServerBuilder()
        {
            Configuration = new Dictionary<string, string>();
        }

        /// <summary>
        /// In Memory representation of Config Settings
        /// </summary>
        public Dictionary<string, string> Configuration { get; private set; }

        /// <summary>
        /// Xunit.ITestOutputHelper is used as Logging-Target (Microsoft.Extensions.Logging)
        /// </summary>
        /// <param name="helper"></param>
        /// <param name="minimumLevel"></param>
        /// <returns></returns>
        public TestServerBuilder TraceToTestOutputHelper(ITestOutputHelper helper, LogLevel minimumLevel)
        {
            _helper = helper ?? throw new ArgumentNullException(nameof(helper));
            _minimumLevel = minimumLevel;
            return this;
        }

        /// <summary>
        /// Test Server Builder instance that uses a empty subfolder of System.IO.Path.GetTempPath
        /// </summary>
        /// <returns></returns>
        public static TestServerBuilder Create()
        {
            return new TestServerBuilder().UseEmptyTempFolder();
        }


        /// <summary>
        /// Creates a subdirectory (Path.GetTempPath() + Guid) and uses this as location for
        /// - Sqlite file  => .\BaGet.db 
        /// - FilePackageStorageService => .\Packages\*.*
        /// </summary>
        /// <returns></returns>
        private TestServerBuilder UseEmptyTempFolder()
        {
            Configuration.Add(DatabaseTypeKey, DatabaseType.Sqlite.ToString());
            var uniqueTempFolder = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(uniqueTempFolder);
            var resolvedSqliteFile = Path.Combine(uniqueTempFolder, "BaGet.db");
            var storageFolderPath = Path.Combine(uniqueTempFolder, DefaultPackagesFolderName);
            Configuration.Add(ConnectionStringKey, string.Format("Data Source={0}", resolvedSqliteFile));
            Configuration.Add(StorageTypeKey, StorageType.FileSystem.ToString());
            Configuration.Add(FileSystemStoragePathKey, storageFolderPath);
            Configuration.Add(SearchTypeKey, nameof(SearchType.Database));
            Configuration.Add(MirrorEnabledKey, false.ToString());
            return this;
        }

        public TestServer Build()
        {
            var configurationBuilder = new ConfigurationBuilder().AddInMemoryCollection(Configuration);
            var hostBuilder = new WebHostBuilder()
                .UseConfiguration(configurationBuilder.Build())
                .UseStartup<Startup>();

            if (_helper != null)
            {
                hostBuilder.ConfigureLogging((builder) =>
                {
                    builder.AddProvider(new XunitLoggerProvider(_helper));
                    builder.SetMinimumLevel(_minimumLevel);
                });
            }

            var server = new TestServer(hostBuilder);

            //Ensure that the Database is created, we use the same feature like inside the Startup in case of Env.IsDevelopment (EF-Migrations)
            var scopeFactory = server.Host.Services.GetRequiredService<IServiceScopeFactory>();
            using (var scope = scopeFactory.CreateScope())
            {
                scope.ServiceProvider
                    .GetRequiredService<IContext>()
                    .Database
                    .Migrate();
            }
            return server;
        }
    }
}
