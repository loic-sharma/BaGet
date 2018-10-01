﻿using BaGet.Core.Mirror;
using BaGet.Extensions;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BaGet
{
    public class Program
    {
        public static void Main(string[] args)
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
                    downloads.OnExecute(async () =>
                    {
                        var provider = CreateHostBuilder(args).Build().Services;

                        await provider
                            .GetRequiredService<DownloadsImporter>()
                            .ImportAsync();
                    });
                });
            });

            app.OnExecute(() =>
            {
                CreateWebHostBuilder(args).Build().Run();
            });

            app.Execute(args);
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .UseUrls("http://0.0.0.0:9090")
                .UseKestrel(options =>
                {
                    // Remove the upload limit from Kestrel. If needed, an upload limit can
                    // be enforced by a reverse proxy server, like IIS.
                    options.Limits.MaxRequestBodySize = null;
                });

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return new HostBuilder()
                .ConfigureBaGetConfiguration(args)
                .ConfigureBaGetServices()
                .ConfigureBaGetLogging();
        }
    }
}
