using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Hosting;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore;

namespace BaGet
{
    using Microsoft.Extensions.Options;

    public class Program
    {
        public static async Task Main(string[] args)
        {
            var app = new CommandLineApplication
            {
                Name = "baget",
                Description = "A light-weight NuGet service",
            };

            app.HelpOption(true);

            app.Command("import", import =>
            {
                import.Command("downloads", downloads =>
                {
                    downloads.OnExecuteAsync(async cancellationToken =>
                    {
                        var host = CreateHostBuilder(args).Build();
                        var importer = host.Services.GetRequiredService<DownloadsImporter>();

                        await importer.ImportAsync(cancellationToken);
                    });
                });
            });

            app.OnExecuteAsync(async cancellationToken =>
            {
                var host = CreateWebHostBuilder(args).Build();

                // Todo: Don't know how to get the options without rebuilding the host!
                var baGetOptions = host.Services.GetRequiredService<IOptions<BaGetOptions>>();
                host = CreateWebHostBuilder(args).UseUrls(baGetOptions.Value.Port).Build();

                await host.RunMigrationsAsync(cancellationToken);
                await host.RunAsync(cancellationToken);
            });

            await app.ExecuteAsync(args);
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            CreateHostBuilder(args).ConfigureKestrel(options =>
                {
                    // Remove the upload limit from Kestrel. If needed, an upload limit can
                    // be enforced by a reverse proxy server, like IIS.
                    options.Limits.MaxRequestBodySize = null;
                }).UseStartup<Startup>();

        public static IWebHostBuilder CreateHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseBaGet();
    }
}
