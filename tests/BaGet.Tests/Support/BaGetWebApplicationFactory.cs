using System;
using System.Collections.Generic;
using System.IO;
using BaGet.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    public class BaGetWebApplicationFactory : WebApplicationFactory<Startup>
    {
        public WebApplicationFactory<Startup> WithOutput(ITestOutputHelper output)
        {
            return WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging =>
                {
                    logging.AddProvider(new XunitLoggerProvider(output));
                });
            });
        }

        public WebApplicationFactory<Startup> WithOutputAndConfig(
            ITestOutputHelper output,
            IEnumerable<KeyValuePair<string, string>> configuration)
        {
            return WithWebHostBuilder(builder =>
            {
                builder
                    .ConfigureAppConfiguration(config =>
                    {
                        config.AddInMemoryCollection(configuration);
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.AddProvider(new XunitLoggerProvider(output));
                    });
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Create temporary storage paths.
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                "BaGetTests",
                Guid.NewGuid().ToString("N"));
            var sqlitePath = Path.Combine(tempPath, "BaGet.db");
            var storagePath = Path.Combine(tempPath, "Packages");

            Directory.CreateDirectory(tempPath);

            builder
                .UseStartup<Startup>()
                .ConfigureAppConfiguration(config =>
                {
                    // Setup the integration test configuration.
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "Database:Type", "Sqlite" },
                        { "Database:ConnectionString", $"Data Source={sqlitePath}" },
                        { "Storage:Type", "FileSystem" },
                        { "Storage:Path", storagePath },
                        { "Search:Type", "Database" },
                        { "Mirror:Enabled", "false" },
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    // Setup the integration test database.
                    var provider = services.BuildServiceProvider();
                    var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                    using (var scope = scopeFactory.CreateScope())
                    {
                        scope.ServiceProvider
                            .GetRequiredService<IContext>()
                            .Database
                            .Migrate();
                    }
                });
        }
    }
}
