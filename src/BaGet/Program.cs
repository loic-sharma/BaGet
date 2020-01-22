using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Extensions;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var app = new CommandLineApplication
            {
                Name = "baget",
                Description = "A light-weight NuGet service",
            };

            app.HelpOption(inherited: true);

            app.Command("import", import =>
            {
                import.Command("downloads", downloads =>
                {
                    downloads.OnExecuteAsync(async cancellationToken =>
                    {
                        var provider = CreateHostBuilder(args).Build().Services;

                        await provider
                            .GetRequiredService<DownloadsImporter>()
                            .ImportAsync(cancellationToken);
                    });
                });
            });

            app.OnExecuteAsync(async cancellationToken =>
            {
                var host = CreateWebHostBuilder(args).Build();

                await host.RunMigrationsAsync(cancellationToken);
                await host.RunAsync(cancellationToken);
            });

            await app.ExecuteAsync(args);
        }

        public static IHostBuilder CreateWebHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .ConfigureKestrel(options =>
                        {
                            // Remove the upload limit from Kestrel. If needed, an upload limit can
                            // be enforced by a reverse proxy server, like IIS.
                            options.Limits.MaxRequestBodySize = null;
                        })
                        .UseStartup<Startup>();
                })
                .ConfigureAppConfiguration((builderContext, config) =>
                {
                    var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");
                    if (!string.IsNullOrEmpty(root))
                    {
                        config.SetBasePath(root);
                    }
                });

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            // TODO: Merge 'CreateWebHostBuilder' and 'CreateHostBuilder'
            // See: https://docs.microsoft.com/en-us/aspnet/core/migration/22-to-30?view=aspnetcore-3.1&tabs=visual-studio#configuration
            return new HostBuilder()
                .ConfigureBaGetConfiguration(args)
                .ConfigureBaGetServices()
                .ConfigureBaGetLogging();
        }
    }
}
