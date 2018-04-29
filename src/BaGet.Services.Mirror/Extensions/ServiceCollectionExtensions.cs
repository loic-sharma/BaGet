using System;
using System.Net;
using System.Net.Http;
using BaGet.Core.Configuration;
using BaGet.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Services.Mirror.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add the services that mirror an upstream package source.
        /// </summary>
        /// <typeparam name="TPackageService">
        /// The local package service. It will be queried first to see if the package exists locally
        /// before falling back to the upstream package source.
        /// </typeparam>
        /// <param name="services">The defined services.</param>
        public static void AddMirrorServices(this IServiceCollection services)
        {
            services.AddTransient<IMirrorService>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                if (!options.Mirror.EnableReadThroughCaching)
                {
                    return new FakeMirrorService();
                }

                return new MirrorService(
                    options.Mirror.PackageSource,
                    provider.GetRequiredService<IPackageService>(),
                    provider.GetRequiredService<IPackageDownloader>(),
                    provider.GetRequiredService<IIndexingService>(),
                    provider.GetRequiredService<ILogger<MirrorService>>());
            });

            services.AddTransient<IPackageDownloader, PackageDownloader>();

            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                var client = new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
                });

                client.Timeout = TimeSpan.FromSeconds(options.Mirror.PackageDownloadTimeoutSeconds);

                return client;
            });
        }
    }
}
