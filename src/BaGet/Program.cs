using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Hosting;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BaGet
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            var env = host.Services.GetRequiredService<IWebHostEnvironment>();
            var config = host.Services.GetRequiredService<IConfiguration>();
            Console.WriteLine(env.ContentRootPath);

            if (!host.ValidateStartupOptions())
            {
                return;
            }

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
                        using (var scope = host.Services.CreateScope())
                        {
                            var importer = scope.ServiceProvider.GetRequiredService<DownloadsImporter>();

                            await importer.ImportAsync(cancellationToken);
                        }
                    });
                });
            });

            app.OnExecuteAsync(async cancellationToken =>
            {
                await host.RunMigrationsAsync(cancellationToken);
                await host.RunAsync(cancellationToken);
            });

            await app.ExecuteAsync(args);
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host
                .CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, config) =>
                {
                    var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");

                    if (!string.IsNullOrEmpty(root))
                    {
                        config.SetBasePath(root);
                    }
                })
                .ConfigureWebHostDefaults(web =>
                {
                    // Normally ASP.NET Core uses the current working directory as the content root.
                    // BaGet can be installed as a .NET Core tool, so we'll set the content root to
                    // the application's root instead.
                    var applicationRoot = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

                    web.UseContentRoot(applicationRoot);

                    // Remove the upload limit from Kestrel. If needed, an upload limit can
                    // be enforced by a reverse proxy server, like IIS.
                    web.ConfigureKestrel(options =>
                    {
                        options.Limits.MaxRequestBodySize = null;
                    });

                    // Let BaGet's configurations override the URL to listen to.
                    var config = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: true)
                        .AddCommandLine(args)
                        .Build();

                    var urls = config["Urls"];

                    if (!string.IsNullOrWhiteSpace(urls))
                    {
                        web.UseUrls(urls);
                    }

                    // Register our startup handler.
                    web.UseStartup<Startup>();
                });
        }
    }
}
