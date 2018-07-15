using System;
using System.IO;
using System.Collections.Generic;
using BaGet.Core.Entities;
using BaGet.Core.Configuration;
using BaGet.Core.Services;
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
        /// <summary>
        /// private/hidden Constructor.
        /// Tests should use some of the static methods!
        /// </summary>
        private TestServerBuilder()
        {
            Configuration = new Dictionary<string, string>();
        }

        private ITestOutputHelper _helper;
        private LogLevel _minimumLevel = LogLevel.None;

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
        /// Not configured Test Server Builder
        /// </summary>
        /// <returns></returns>
        public static TestServerBuilder Create()
        {
            return new TestServerBuilder();
        }

        private readonly string DatabaseTypeKey = $"{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.Type)}";
        private readonly string ConnectionStringKey = $"{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.ConnectionString)}";
        private readonly string StorageTypeKey = $"{nameof(BaGetOptions.Storage)}:{nameof(StorageOptions.Type)}";
        private readonly string MirrorEnableReadThroughCachingKey = $"{nameof(BaGetOptions.Mirror)}:{nameof(MirrorOptions.EnableReadThroughCaching)}";
        private readonly string FileSystemStoragePathKey = $"{nameof(BaGetOptions.Storage)}:{nameof(FileSystemStorageOptions.Path)}";

        /// <summary>
        /// Creates a subdirectory: Path.GetTempPath() + Guid  
        /// and uses this as location for 
        /// - Sqlite file  => .\BaGet.db 
        /// - FilePackageStorageService => .\Packages\*.*
        /// </summary>
        /// <returns></returns>
        public TestServerBuilder UseEmptyTempFolder()
        {
            Configuration.Add(DatabaseTypeKey, DatabaseType.Sqlite.ToString());
            string uniqueTempFolder = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(uniqueTempFolder);
            string resolvedSqliteFile = Path.Combine(uniqueTempFolder, "BaGet.db");
            string storageFolderPath = Path.Combine(uniqueTempFolder, FilePackageStorageService.DefaultPackagesFolderName);

            Configuration.Add(ConnectionStringKey, string.Format("Data Source={0}", resolvedSqliteFile));
            Configuration.Add(StorageTypeKey, StorageType.FileSystem.ToString());
            Configuration.Add(MirrorEnableReadThroughCachingKey, false.ToString());
            Configuration.Add(FileSystemStoragePathKey, storageFolderPath);
            return this;
        }

        public TestServer Build()
        {
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder().AddInMemoryCollection(this.Configuration);
            IWebHostBuilder hostBuilder = new WebHostBuilder()
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

            TestServer server = new TestServer(hostBuilder);

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
