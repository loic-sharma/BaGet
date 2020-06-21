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
    public static class DependencyInjectionExtensions
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

            app.AddConfiguration();
            app.AddCoreServices();
            app.AddDefaultProviders();
            app.AddMirrorServices();
            app.AddSearchServices();
            app.AddStorageServices();

            configureAction(app);

            return services;
        }

        /// <summary>
        /// Add a new provider to the dependency injection container. The provider may
        /// provide an implementation of the service, or it may return null.
        /// </summary>
        /// <typeparam name="TService">The service that may be provided.</typeparam>
        /// <param name="services">The dependency injection container.</param>
        /// <param name="func">A handler that provides the service, or null.</param>
        /// <returns>The dependency injection container.</returns>
        public static IServiceCollection AddProvider<TService>(
            this IServiceCollection services,
            Func<IServiceProvider, IConfiguration, TService> func)
            where TService : class
        {
            services.AddSingleton<IProvider<TService>>(new DelegateProvider<TService>(func));

            return services;
        }

        // TODO: Convert everything over to new thing...
        public static IServiceCollection ConfigureAndValidate<TOptions>(
            this IServiceCollection services,
            IConfiguration config)
          where TOptions : class
        {
            services.Configure<TOptions>(config);
            services.TryAddSingleton<IPostConfigureOptions<TOptions>, ValidatePostConfigureOptions<TOptions>>();

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

        private static void AddConfiguration(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<BaGetOptions>();
            app.Services.AddBaGetOptions<DatabaseOptions>(nameof(BaGetOptions.Database));
            app.Services.AddBaGetOptions<FileSystemStorageOptions>(nameof(BaGetOptions.Storage));
            app.Services.AddBaGetOptions<MirrorOptions>(nameof(BaGetOptions.Mirror));
            app.Services.AddBaGetOptions<SearchOptions>(nameof(BaGetOptions.Search));
            app.Services.AddBaGetOptions<StorageOptions>(nameof(BaGetOptions.Storage));
        }

        private static void AddCoreServices(this BaGetApplication app)
        {
            app.Services.TryAddTransient<IAuthenticationService, ApiKeyAuthenticationService>();
            app.Services.TryAddTransient<IPackageIndexingService, PackageIndexingService>();
            app.Services.TryAddTransient<IPackageDeletionService, PackageDeletionService>();
            app.Services.TryAddTransient<ISymbolIndexingService, SymbolIndexingService>();
            app.Services.TryAddTransient<IServiceIndexService, BaGetServiceIndex>();
            app.Services.TryAddTransient<IPackageContentService, DefaultPackageContentService>();
            app.Services.TryAddTransient<IPackageMetadataService, DefaultPackageMetadataService>();
            app.Services.TryAddSingleton<IFrameworkCompatibilityService, FrameworkCompatibilityService>();
            app.Services.TryAddSingleton<RegistrationBuilder>();
            app.Services.TryAddTransient<PackageService>();
        }

        private static void AddMirrorServices(this BaGetApplication app)
        {
            app.Services.TryAddTransient<NullMirrorService>();
            app.Services.TryAddTransient<MirrorService>();
            app.Services.TryAddSingleton<NuGetClient>();
            app.Services.TryAddScoped<DownloadsImporter>();
            app.Services.TryAddScoped<IPackageDownloadsSource, PackageDownloadsJsonSource>();

            app.Services.TryAddTransient(provider =>
            {
                var options = provider.GetRequiredService<IOptionsSnapshot<MirrorOptions>>();
                var service = options.Value.Enabled ? typeof(MirrorService) : typeof(NullMirrorService);

                return (IMirrorService)provider.GetRequiredService(service);
            });

            app.Services.TryAddSingleton(provider =>
            {
                var httpClient = provider.GetRequiredService<HttpClient>();
                var options = provider.GetRequiredService<IOptions<MirrorOptions>>();

                return new NuGetClientFactory(
                    httpClient,
                    options.Value.PackageSource.ToString());
            });

            app.Services.TryAddSingleton(provider =>
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
            });
        }

        private static void AddSearchServices(this BaGetApplication app)
        {
            app.Services.AddTransient<DatabaseSearchService>();
            app.Services.AddSingleton<NullSearchService>();
            app.Services.AddSingleton<NullSearchIndexer>();
        }

        private static void AddStorageServices(this BaGetApplication app)
        {
            app.Services.TryAddSingleton<NullStorageService>();
            app.Services.TryAddTransient<FileStorageService>();
            app.Services.TryAddTransient<IPackageStorageService, PackageStorageService>();
            app.Services.TryAddTransient<ISymbolStorageService, SymbolStorageService>();
        }

        private static void AddDefaultProviders(this BaGetApplication app)
        {
            app.Services.AddProvider((provider, configuration) =>
            {
                if (!configuration.HasSearchType("null")) return null;

                return provider.GetRequiredService<NullSearchService>();
            });

            app.Services.AddProvider((provider, configuration) =>
            {
                if (!configuration.HasSearchType("null")) return null;

                return provider.GetRequiredService<NullSearchIndexer>();
            });

            app.Services.AddProvider<IStorageService>((provider, configuration) =>
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

        public static T GetServiceFromProviders<T>(IServiceProvider services) where T : class
        {
            // Run through all the providers for the type. Find the first provider that results a non-null result.
            var providers = services.GetRequiredService<IEnumerable<IProvider<T>>>();
            var configuration = services.GetRequiredService<IConfiguration>();

            foreach (var provider in providers)
            {
                var result = provider.GetOrNull(services, configuration);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
