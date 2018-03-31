using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Configuration;
using BaGet.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Tools.ImportDownloads
{
    public class Program
    {
        public static void Main(string[] args)
            => MainAsync(args)
                .GetAwaiter()
                .GetResult();

        private static Task MainAsync(string[] args)
            => GetServiceProvider(GetConfiguration())
                .GetRequiredService<DownloadsImporter>()
                .ImportAsync();

        private static IConfiguration GetConfiguration()
            => new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json")
                .Build();

        private static IServiceProvider GetServiceProvider(IConfiguration configuration)
        {
            var services = new ServiceCollection();

            services.AddBaGetContext();
            services.Configure<BaGetOptions>(configuration);

            services.AddLogging(logging => logging.AddConsole());

            services.AddSingleton<HttpClient>();
            services.AddSingleton<DownloadsImporter>();
            services.AddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

            return services.BuildServiceProvider();
        }
    }
}
