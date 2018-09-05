using BaGet.Core.Configuration;
using BaGet.Core.Mirror;
using BaGet.Extensions;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

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

            app.Command("mirror", mirror =>
            {
                mirror.Description = "Mirror packages or downloads from an upstream source";

                mirror.Command("all", all =>
                {
                    all.Description = "Mirror all packages from the upstream source or local feed";

                    var source = all.Argument("source", "The upstream source or feed").IsRequired();

                    all.OnExecute(() =>
                    {
                        using (var host = CreateHostBuilder(args).Build())
                        {
                            var logger = host.Services.GetRequiredService<ILogger<Program>>();

                            logger.LogInformation("TODO: Mirror {Source}", source.Value);
                        }
                    });
                });

                mirror.Command("package", package =>
                {
                    package.Description = "Mirror a single package from the configured upstream source";

                    var id = package.Argument("id", "The package identifier").IsRequired();
                    var versionString = package.Argument("version", "The package version").IsRequired();

                    package.OnExecute(async () =>
                    {
                        using (var host = CreateHostBuilder(args).Build())
                        {
                            // TODO: Instead of using config, allow the CLI to set upstream source.
                            // TODO: Validate mirror options?
                            var provider = host.Services;
                            var logger = provider.GetRequiredService<ILogger<Program>>();
                            var mirrorOptions = provider
                                .GetRequiredService<IOptions<BaGetOptions>>()
                                .Value
                                .Mirror;

                            if (!mirrorOptions.Enabled)
                            {
                                logger.LogError(
                                    $"Cannot mirror upstream source as '{nameof(BaGetOptions.Mirror)}:" +
                                    $"{nameof(MirrorOptions.Enabled)}' is configured as 'false'.");
                                return;
                            }

                            // TODO: Make NuGetVersion an IOptionValidator
                            // https://github.com/natemcmaster/CommandLineUtils/blob/f498cc7383b27730fd24486510573ab61ccab9d6/samples/Validation/BuilderApi.cs#L54
                            if (!NuGetVersion.TryParse(versionString.Value, out var version))
                            {
                                logger.LogError("'{Version}' is not a valid version", versionString.Value);
                                return;
                            }

                            // TODO: Output more information
                            await provider
                                .GetRequiredService<IMirrorService>()
                                .MirrorAsync(id.Value, version);
                        }
                    });
                });

                mirror.Command("downloads", downloads =>
                {
                    downloads.Description = "Mirror package downloads from nuget.org";

                    downloads.OnExecute(async () =>
                    {
                        using (var host = CreateHostBuilder(args).Build())
                        {
                            var importer = host.Services.GetRequiredService<DownloadsImporter>();

                            await importer.ImportAsync();
                        }
                    });
                });
            });

            app.OnExecute(() =>
            {
                CreateWebHostBuilder(args).Build().Run();
            });

            app.Execute(args);
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            return WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .UseKestrel(options =>
                {
                    options.Limits.MaxRequestBodySize = null;
                });
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return new HostBuilder()
                .ConfigureBaGetConfiguration(args)
                .ConfigureBaGetServices()
                .ConfigureBaGetLogging();
        }
    }
}
