using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using BaGet.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    public static partial class DependencyInjectionExtensions
    {
        public static IServiceCollection AddBaGetApplication(
            this IServiceCollection services,
            Action<BaGetApplication> configureAction)
        {
            var app = new BaGetApplication(services);

            services.AddConfiguration();
            services.AddBaGetServices();
            services.AddDefaultProviders();

            configureAction(app);

            services.AddFallbackServices();

            return services;
        }

        /// <summary>
        /// Configures and validates options.
        /// </summary>
        /// <typeparam name="TOptions">The options type that should be added.</typeparam>
        /// <param name="services">The dependency injection container to add options.</param>
        /// <param name="key">
        /// The configuration key that should be used when configuring the options.
        /// If null, the root configuration will be used to configure the options.
        /// </param>
        /// <returns>The dependency injection container.</returns>
        public static IServiceCollection AddBaGetOptions<TOptions>(
            this IServiceCollection services,
            string key = null)
            where TOptions : class
        {
            services.AddSingleton<IValidateOptions<TOptions>>(new ValidateBaGetOptions<TOptions>(key));
            services.AddSingleton<IConfigureOptions<TOptions>>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                if (key != null)
                {
                    config = config.GetSection(key);
                }

                return new BindOptions<TOptions>(config);
            });

            return services;
        }

        private static void AddConfiguration(this IServiceCollection services)
        {
            services.AddBaGetOptions<BaGetOptions>();
            services.AddBaGetOptions<DatabaseOptions>(nameof(BaGetOptions.Database));
            services.AddBaGetOptions<FileSystemStorageOptions>(nameof(BaGetOptions.Storage));
            services.AddBaGetOptions<MirrorOptions>(nameof(BaGetOptions.Mirror));
            services.AddBaGetOptions<SearchOptions>(nameof(BaGetOptions.Search));
            services.AddBaGetOptions<StorageOptions>(nameof(BaGetOptions.Storage));
        }

        private static void AddBaGetServices(this IServiceCollection services)
        {
            services.TryAddSingleton<IFrameworkCompatibilityService, FrameworkCompatibilityService>();
            services.TryAddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

            services.TryAddSingleton<ISearchResponseBuilder, SearchResponseBuilder>();
            services.TryAddSingleton<NuGetClient>();
            services.TryAddSingleton<NullSearchIndexer>();
            services.TryAddSingleton<NullSearchService>();
            services.TryAddSingleton<RegistrationBuilder>();
            services.TryAddSingleton<SystemTime>();
            services.TryAddSingleton<ValidateStartupOptions>();

            services.TryAddSingleton(HttpClientFactory);
            services.TryAddSingleton(NuGetClientFactoryFactory);

            services.TryAddScoped<DownloadsImporter>();

            services.TryAddTransient<IAuthenticationService, ApiKeyAuthenticationService>();
            services.TryAddTransient<IPackageContentService, DefaultPackageContentService>();
            services.TryAddTransient<IPackageDeletionService, PackageDeletionService>();
            services.TryAddTransient<IPackageIndexingService, PackageIndexingService>();
            services.TryAddTransient<IPackageMetadataService, DefaultPackageMetadataService>();
            services.TryAddTransient<IPackageService, PackageService>();
            services.TryAddTransient<IPackageStorageService, PackageStorageService>();
            services.TryAddTransient<IServiceIndexService, BaGetServiceIndex>();
            services.TryAddTransient<ISymbolIndexingService, SymbolIndexingService>();
            services.TryAddTransient<ISymbolStorageService, SymbolStorageService>();

            services.TryAddTransient<DatabaseSearchService>();
            services.TryAddTransient<FileStorageService>();
            services.TryAddTransient<PackageService>();
            services.TryAddTransient<V2UpstreamClient>();
            services.TryAddTransient<V3UpstreamClient>();
            services.TryAddTransient<DisabledUpstreamClient>();
            services.TryAddSingleton<NullStorageService>();
            services.TryAddTransient<PackageDatabase>();

            services.TryAddTransient(UpstreamClientFactory);
        }

        private static void AddDefaultProviders(this IServiceCollection services)
        {
            services.AddProvider((provider, configuration) =>
            {
                if (!configuration.HasSearchType("null")) return null;

                return provider.GetRequiredService<NullSearchService>();
            });

            services.AddProvider((provider, configuration) =>
            {
                if (!configuration.HasSearchType("null")) return null;

                return provider.GetRequiredService<NullSearchIndexer>();
            });

            services.AddProvider<IStorageService>((provider, configuration) =>
            {
                if (configuration.HasStorageType("filesystem"))
                {
                    return provider.GetRequiredService<FileStorageService>();
                }

                if (configuration.HasStorageType("null"))
                {
                    return provider.GetRequiredService<NullStorageService>();
                }

                return null;
            });
        }

        private static void AddFallbackServices(this IServiceCollection services)
        {
            services.TryAddScoped<IContext, NullContext>();

            // BaGet's services have multiple implementations that live side-by-side.
            // The application will choose the implementation using one of two ways:
            //
            // 1. Using the first implementation that was registered in the dependency injection
            //    container. This is the strategy used by applications that embed BaGet.
            // 2. Using "providers". The providers will examine the application's configuration to
            //    determine whether its service implementation is active. Thsi is the strategy used
            //    by the default BaGet application.
            //
            // BaGet has database and search services, but the database services are special
            // in that they may also act as search services. If an application registers the
            // database service first and the search service second, the application should
            // use the search service even though it wasn't registered first. Furthermore,
            // if an application registers a database service without a search service, the
            // database service should be used for search. This effect is achieved by deferring
            // the database search service's registration until the very end.
            services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
            services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<DatabaseSearchService>());
        }

        private static HttpClient HttpClientFactory(IServiceProvider provider)
        {
            var options = provider.GetRequiredService<IOptions<MirrorOptions>>().Value;

            var assembly = Assembly.GetEntryAssembly();
            var assemblyName = assembly.GetName().Name;
            var assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

            var client = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

            client.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
            client.Timeout = TimeSpan.FromSeconds(options.PackageDownloadTimeoutSeconds);

            return client;
        }

        private static NuGetClientFactory NuGetClientFactoryFactory(IServiceProvider provider)
        {
            var httpClient = provider.GetRequiredService<HttpClient>();
            var options = provider.GetRequiredService<IOptions<MirrorOptions>>();

            return new NuGetClientFactory(
                httpClient,
                options.Value.PackageSource.ToString());
        }

        private static IUpstreamClient UpstreamClientFactory(IServiceProvider provider)
        {
            var options = provider.GetRequiredService<IOptionsSnapshot<MirrorOptions>>();

            // TODO: Convert to switch expression.
            if (!options.Value.Enabled)
            {
                return provider.GetRequiredService<DisabledUpstreamClient>();
            }

            else if (options.Value.Legacy)
            {
                return provider.GetRequiredService<V2UpstreamClient>();
            }

            else
            {
                return provider.GetRequiredService<V3UpstreamClient>();
            }
        }
    }
}
