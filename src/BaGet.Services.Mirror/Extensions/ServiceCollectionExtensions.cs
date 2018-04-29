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
        public static void AddMirrorServices<TPackageService>(this IServiceCollection services)
            where TPackageService : class, IPackageService
        {
            /// Both the default indexing service and the mirror package service depend on
            /// an <see cref="IPackageService"/>. To prevent infinite recursion, we will
            /// force the indexing service to use the <see cref="TPackageService"/>.
            services.AddTransient<MirrorIndexingService<TPackageService>>();

            services.AddTransient(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                return new MirrorPackageService<TPackageService>(
                    options.Mirror.PackageSource,
                    provider.GetRequiredService<TPackageService>(),
                    provider.GetRequiredService<IPackageDownloader>(),
                    provider.GetRequiredService<IIndexingService>(),
                    provider.GetRequiredService<ILogger<MirrorPackageService<TPackageService>>>());
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
