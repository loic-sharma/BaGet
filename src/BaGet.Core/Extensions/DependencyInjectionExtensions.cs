using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using BaGet.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    public static partial class DependencyInjectionExtensions
    {
        private static readonly string DatabaseTypeKey = $"{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.Type)}";
        private static readonly string SearchTypeKey = $"{nameof(BaGetOptions.Search)}:{nameof(SearchOptions.Type)}";
        private static readonly string StorageTypeKey = $"{nameof(BaGetOptions.Storage)}:{nameof(StorageOptions.Type)}";

        private static readonly string DatabaseSearchType = "Database";

        public static IServiceCollection AddBaGetApplication(
            this IServiceCollection services,
            Action<BaGetApplication> configureAction)
        {
            var app = new BaGetApplication(services);

            services.AddConfiguration();
            services.AddBaGetServices();
            services.AddDefaultProviders();

            configureAction(app);

            return services;
        }

        public static void AddFilesystem(this BaGetApplication app)
        {
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<FileStorageService>());
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

                return new ConfigureBaGetOptions<TOptions>(config);
            });

            return services;
        }

        public static IServiceCollection AddBaGetDbContextProvider<TContext>(
            this IServiceCollection services,
            string databaseType,
            Action<IServiceProvider, DbContextOptionsBuilder> configureContext)
            where TContext : DbContext, IContext
        {
            services.TryAddScoped<IContext>(provider => provider.GetRequiredService<TContext>());
            services.TryAddTransient<IPackageService>(provider => provider.GetRequiredService<PackageService>());
            services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
            services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<DatabaseSearchService>());

            services.AddDbContext<TContext>(configureContext);

            services.AddProvider<IContext>((provider, config) =>
            {
                if (!config.HasDatabaseType(databaseType)) return null;

                return provider.GetRequiredService<TContext>();
            });

            services.AddProvider<IPackageService>((provider, config) =>
            {
                if (!config.HasDatabaseType(databaseType)) return null;

                return provider.GetRequiredService<PackageService>();
            });

            services.AddProvider<ISearchIndexer>((provider, config) =>
            {
                if (!config.HasSearchType(DatabaseSearchType)) return null;
                if (!config.HasDatabaseType(databaseType)) return null;

                return provider.GetRequiredService<NullSearchIndexer>();
            });

            services.AddProvider<ISearchService>((provider, config) =>
            {
                if (!config.HasSearchType(DatabaseSearchType)) return null;
                if (!config.HasDatabaseType(databaseType)) return null;

                return provider.GetRequiredService<DatabaseSearchService>();
            });

            return services;
        }

        public static bool HasDatabaseType(this IConfiguration config, string value)
        {
            return config[DatabaseTypeKey].Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasSearchType(this IConfiguration config, string value)
        {
            return config[SearchTypeKey].Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasStorageType(this IConfiguration config, string value)
        {
            return config[StorageTypeKey].Equals(value, StringComparison.OrdinalIgnoreCase);
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

            services.TryAddSingleton<NuGetClient>();
            services.TryAddSingleton<NullSearchIndexer>();
            services.TryAddSingleton<NullSearchService>();
            services.TryAddSingleton<RegistrationBuilder>();

            services.TryAddSingleton(HttpClientFactory);
            services.TryAddSingleton(NuGetClientFactoryFactory);

            services.TryAddScoped<DownloadsImporter>();

            services.TryAddTransient<IAuthenticationService, ApiKeyAuthenticationService>();
            services.TryAddTransient<IPackageContentService, DefaultPackageContentService>();
            services.TryAddTransient<IPackageDeletionService, PackageDeletionService>();
            services.TryAddTransient<IPackageIndexingService, PackageIndexingService>();
            services.TryAddTransient<IPackageMetadataService, DefaultPackageMetadataService>();
            services.TryAddTransient<IPackageStorageService, PackageStorageService>();
            services.TryAddTransient<IServiceIndexService, BaGetServiceIndex>();
            services.TryAddTransient<ISymbolIndexingService, SymbolIndexingService>();
            services.TryAddTransient<ISymbolStorageService, SymbolStorageService>();

            services.TryAddTransient<DatabaseSearchService>();
            services.TryAddTransient<FileStorageService>();
            services.TryAddTransient<MirrorService>();
            services.TryAddTransient<NullMirrorService>();
            services.TryAddSingleton<NullStorageService>();
            services.TryAddTransient<PackageService>();

            services.TryAddTransient(IMirrorServiceFactory);
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

        private static HttpClient HttpClientFactory(IServiceProvider provider)
        {
            var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

            var assembly = Assembly.GetEntryAssembly();
            var assemblyName = assembly.GetName().Name;
            var assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

            var client = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

            client.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
            client.Timeout = TimeSpan.FromSeconds(options.Mirror.PackageDownloadTimeoutSeconds);

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

        private static IMirrorService IMirrorServiceFactory(IServiceProvider provider)
        {
            var options = provider.GetRequiredService<IOptionsSnapshot<MirrorOptions>>();
            var service = options.Value.Enabled ? typeof(MirrorService) : typeof(NullMirrorService);

            return (IMirrorService)provider.GetRequiredService(service);
        }
    }
}
