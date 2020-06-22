using System;
using System.IO;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Hosting;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
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
                        var host = CreatCmdHostBuilder(args).Build();
                        var importer = host.Services.GetRequiredService<DownloadsImporter>();
                        await importer.ImportAsync(cancellationToken);
                    });
                });
            });

            app.OnExecuteAsync(async cancellationToken =>
            {
                var host = CreateHostBuilder(args).Build();

                await host.RunMigrationsAsync(cancellationToken);
                await host.RunAsync(cancellationToken);
            });

            try
            {
                await app.ExecuteAsync(args);
            }
            catch (OptionsValidationException e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid BaGet configurations:");
                Console.WriteLine();

                foreach (var failure in e.Failures)
                {
                    Console.WriteLine(failure);
                }

                Console.ResetColor();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(ConfigureBaGetAppConfiguration)
                .UseBaGet(ConfigureBaGetApplication)
                .ConfigureWebHostDefaults(web =>
                {
                    web.ConfigureKestrel(options =>
                    {
                        // Remove the upload limit from Kestrel. If needed, an upload limit can
                        // be enforced by a reverse proxy server, like IIS.
                        options.Limits.MaxRequestBodySize = null;
                    });

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

                    web.UseStartup<Startup>();
                });

        public static IHostBuilder CreatCmdHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(ConfigureBaGetAppConfiguration)
                .UseBaGet(ConfigureBaGetApplication);

        private static void ConfigureBaGetApplication(BaGetApplication app)
        {
            // You can swap between implementations of subsystems like storage and search using BaGet's configuration.
            // Each subsystem's implementation has a provider that reads the configuration to determine if it should be
            // activated. BaGet will run through all its providers until it finds one that is active.
            // NOTE: Don't copy this if you are embedding BaGet into your own ASP.NET Core application.
            app.Services.AddScoped(DependencyInjectionExtensions.GetServiceFromProviders<IContext>);
            app.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IStorageService>);
            app.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IPackageService>);
            app.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchService>);
            app.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchIndexer>);

            // Add database providers.
            app.AddAzureTableDatabase();
            app.AddMySqlDatabase();
            app.AddPostgreSqlDatabase();
            app.AddSqliteDatabase();
            app.AddSqlServerDatabase();

            // Add storage providers.
            app.AddFileStorage();
            app.AddAliyunOssStorage();
            app.AddAwsS3Storage();
            app.AddAzureBlobStorage();
            app.AddGoogleCloudStorage();

            // Add search providers.
            app.AddAzureSearch();

            // Add strict validation for BaGet's configs.
            app.Services.AddSingleton<IValidateOptions<BaGetOptions>, ValidateBaGetOptions>();
        }

        private static void ConfigureBaGetAppConfiguration(HostBuilderContext ctx, IConfigurationBuilder config)
        {
            var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");

            if (!string.IsNullOrEmpty(root))
            {
                config.SetBasePath(root);
            }
        }
    }
}
