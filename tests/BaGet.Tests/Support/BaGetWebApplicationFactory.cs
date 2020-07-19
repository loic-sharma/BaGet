using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BaGet.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
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
                    // Clobber BaGet services with test replacements.
                    var time = new Mock<SystemTime>();
                    time
                        .Setup(t => t.UtcNow)
                        .Returns(DateTime.Parse("2020-01-01T00:00:00.000Z"));

                    services.AddSingleton(time.Object);

                    // Setup the integration test database.
                    var provider = services.BuildServiceProvider();
                    var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                    using (var scope = scopeFactory.CreateScope())
                    {
                        var ctx = scope.ServiceProvider.GetRequiredService<IContext>();
                        var indexer = scope.ServiceProvider.GetRequiredService<IPackageIndexingService>();
                        var cancellationToken = CancellationToken.None;

                        // Ensure the database is created.
                        ctx.Database.Migrate();

                        // Seed the application with test data.
                        var result = indexer.IndexAsync(PackageData.Default, cancellationToken).Result;
                        if (result != PackageIndexingResult.Success)
                        {
                            throw new InvalidOperationException($"Unexpected indexing result {result}");
                        }
                    }
                });
        }
    }
}
