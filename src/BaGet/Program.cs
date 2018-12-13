using BaGet.Core.Mirror;
using BaGet.Extensions;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Web;

namespace BaGet
{
    public class Program
    {
        static readonly ILogger Logger = NLog.Web.NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();

        public static void Main(string[] args)
        {
            Logger.Info("Starting BaGet...");
            try
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
            finally
            {
                Logger.Info("Shutting down BaGet...");
                NLog.LogManager.Shutdown();
            }
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .UseKestrel(options =>
                {
                    // Remove the upload limit from Kestrel. If needed, an upload limit can
                    // be enforced by a reverse proxy server, like IIS.
                    options.Limits.MaxRequestBodySize = null;
                })
            .UseNLog();

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return new HostBuilder()
                .ConfigureBaGetConfiguration(args)
                .ConfigureBaGetServices()
                .ConfigureBaGetLogging();
        }
    }
}
