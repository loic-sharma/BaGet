using System;
using System.Threading.Tasks;
using BaGet.Configuration;
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
            var provider = GetServiceProvider(GetConfiguration());
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            using (var scope = scopeFactory.CreateScope())
            {
                scope.ServiceProvider
                    .GetRequiredService<IndexerContext>()
                    .Database
                    .Migrate();
            }

            await provider.GetRequiredService<Initializer>().InitializeAsync();
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
            services.AddLogging(logging => logging.AddConsole());

            services.AddBaGetContext();
            services.AddDbContext<IndexerContext>((provider, options) =>
            {
                options.UseSqlite(IndexerContextFactory.ConnectionString);
            });

            services.AddTransient<Initializer>();

            return services.BuildServiceProvider();
        }
    }
}
