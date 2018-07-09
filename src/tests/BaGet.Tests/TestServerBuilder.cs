using System;
using System.IO;
using System.Collections.Generic;
using BaGet.Core.Entities;
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
        }

        private ITestOutputHelper _helper;
        private LogLevel _minimumLevel = LogLevel.None;

        /// <summary>
        /// Xunit.ITestOutputHelper is used as Logging-Target (Microsoft.Extensions.Logging)
        /// </summary>
        /// <param name="helper"></param>
        /// <param name="minimumLevel"></param>
        /// <returns></returns>
        public TestServerBuilder TraceToTestOutputHelper(ITestOutputHelper helper, LogLevel minimumLevel )
        {
            _helper = helper ?? throw new ArgumentNullException(nameof(helper));
            _minimumLevel = minimumLevel;
            return this;
        }

        /// <summary>
        /// Empty means: 
        ///  - Sqlite Database with a new/empty file
        ///  - StorageType FileSystem based on a empty/temporary directory
        /// </summary>
        /// <returns></returns>
        public static TestServerBuilder Empty()
        {
            return new TestServerBuilder();
        }

        public TestServer Build()
        {
            
            Dictionary<string, string> testHostConfig = new Dictionary<string, string>();
            testHostConfig.Add("Database:Type", "Sqlite");
            string resolvedSqliteFile = Path.GetFullPath("..\\..\\baget.db");

            testHostConfig.Add("Database:ConnectionString", string.Format("Data Source={0}", resolvedSqliteFile));
            testHostConfig.Add("Storage:Type", "FileSystem");
            testHostConfig.Add("Mirror:EnableReadThroughCaching", false.ToString());

            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder().AddInMemoryCollection(testHostConfig);
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
