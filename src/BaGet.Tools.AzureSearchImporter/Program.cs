using System;
using System.Threading.Tasks;
using BaGet.Azure.Extensions;
using BaGet.Core.Configuration;
using BaGet.Core.Services;
using BaGet.Extensions;
using BaGet.Tools.AzureSearchImporter.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Tools.AzureSearchImporter
{
    class Program
    {
        public static void Main(string[] args)
            => MainAsync(args)
                .GetAwaiter()
                .GetResult();

        private async static Task MainAsync(string[] args)
        {
            // Parse the skip from arguments.
            int skip = 0;

            if (args.Length > 0)
            {
                int.TryParse(args[args.Length - 1], out skip);
            }

            // Prepare the job.
            var provider = GetServiceProvider(GetConfiguration());
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            var initializer = provider.GetRequiredService<Initializer>();
            var importer = provider.GetRequiredService<Importer>();

            using (var scope = scopeFactory.CreateScope())
            {
                scope.ServiceProvider
                    .GetRequiredService<IndexerContext>()
                    .Database
                    .Migrate();
            }

            // Initialize the state and start importing packages to the search index.
            await initializer.InitializeAsync();
            await importer.ImportAsync(skip);
        }

        private static IConfiguration GetConfiguration()
            => new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json")
                .Build();

        private static IServiceProvider GetServiceProvider(IConfiguration configuration)
        {
            var services = new ServiceCollection();

            services.Configure<BaGetOptions>(configuration);
            services.ConfigureAzure(configuration);

            services.AddLogging(logging =>
            {
                logging.AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Warning);
                logging.AddConsole();
            });

            services.AddBaGetContext();
            services.AddDbContext<IndexerContext>((provider, options) =>
            {
                options.UseSqlite(IndexerContextFactory.ConnectionString);
            });

            services.AddTransient<IPackageService, PackageService>();
            services.AddAzureSearch();

            services.AddTransient<Initializer>();
            services.AddTransient<Importer>();

            return services.BuildServiceProvider();
        }
    }
}
