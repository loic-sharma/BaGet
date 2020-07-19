using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BaGet.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    public class BaGetWebApplicationFactory : WebApplicationFactory<Startup>
    {
        private readonly Mock<SystemTime> _time;

        public BaGetWebApplicationFactory()
        {
            _time = new Mock<SystemTime>();
            _time
                .Setup(t => t.UtcNow)
                .Returns(DateTime.Parse("2020-01-01T00:00:00.000Z"));
        }

        public WebApplicationFactory<Startup> WithOutput(ITestOutputHelper output)
        {
            return WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging =>
                {
                    // BaGet uses console logging by default. This logger throws operation
                    // cancelled exceptions when the host shuts down, causing the the debugger
                    // to pause repeatedly if CLR exceptions are enabled.
                    logging.ClearProviders();

                    // Pipe logs to the xunit output.
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
                .UseEnvironment("Production")
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
                    // Add mocks for testing purposes.
                    services.AddSingleton(_time.Object);

                    // Setup the integration test database.
                    var provider = services.BuildServiceProvider();
                    var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                    using (var scope = scopeFactory.CreateScope())
                    {
                        // Ensure the database is created before we run migrations. The migrations
                        // can create the database too, however, migrations check whether the database exists
                        // first. The SQLite provider implements this by attempting to open a connection,
                        // and if that fails, creating the database. This throws several exceptions that
                        // pauses the debugger repeatedly if CLR exceptions are enabled.
                        // See: https://github.com/dotnet/efcore/blob/644d3c8c3a604fd0121d90eaf34f14870e19bcff/src/EFCore.Sqlite.Core/Storage/Internal/SqliteDatabaseCreator.cs#L88-L98
                        var ctx = scope.ServiceProvider.GetRequiredService<IContext>();
                        var dbCreator = ctx.Database.GetService<IRelationalDatabaseCreator>();

                        dbCreator.Create();
                        ctx.Database.Migrate();

                        // Seed the application with test data.
                        var indexer = scope.ServiceProvider.GetRequiredService<IPackageIndexingService>();
                        var cancellationToken = CancellationToken.None;

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
