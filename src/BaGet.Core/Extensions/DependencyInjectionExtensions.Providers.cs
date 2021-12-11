using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGet.Core
{
    public static partial class DependencyInjectionExtensions
    {
        private static readonly string DatabaseTypeKey = $"{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.Type)}";
        private static readonly string SearchTypeKey = $"{nameof(BaGetOptions.Search)}:{nameof(SearchOptions.Type)}";
        private static readonly string StorageTypeKey = $"{nameof(BaGetOptions.Storage)}:{nameof(StorageOptions.Type)}";

        private static readonly string DatabaseSearchType = "Database";

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

        /// <summary>
        /// Determine whether a database type is currently active.
        /// </summary>
        /// <param name="config">The application's configuration.</param>
        /// <param name="value">The database type that should be checked.</param>
        /// <returns>Whether the database type is active.</returns>
        public static bool HasDatabaseType(this IConfiguration config, string value)
        {
            return config[DatabaseTypeKey].Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determine whether a search type is currently active.
        /// </summary>
        /// <param name="config">The application's configuration.</param>
        /// <param name="value">The search type that should be checked.</param>
        /// <returns>Whether the search type is active.</returns>
        public static bool HasSearchType(this IConfiguration config, string value)
        {
            return config[SearchTypeKey].Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determine whether a storage type is currently active.
        /// </summary>
        /// <param name="config">The application's configuration.</param>
        /// <param name="value">The storage type that should be checked.</param>
        /// <returns>Whether the database type is active.</returns>
        public static bool HasStorageType(this IConfiguration config, string value)
        {
            return config[StorageTypeKey].Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        public static IServiceCollection AddBaGetDbContextProvider<TContext>(
            this IServiceCollection services,
            string databaseType,
            Action<IServiceProvider, DbContextOptionsBuilder> configureContext)
            where TContext : DbContext, IContext
        {
            services.TryAddScoped<IContext>(provider => provider.GetRequiredService<TContext>());
            services.TryAddTransient<IPackageDatabase>(provider => provider.GetRequiredService<PackageDatabase>());

            services.AddDbContext<TContext>(configureContext);

            services.AddProvider<IContext>((provider, config) =>
            {
                if (!config.HasDatabaseType(databaseType)) return null;

                return provider.GetRequiredService<TContext>();
            });

            services.AddProvider<IPackageDatabase>((provider, config) =>
            {
                if (!config.HasDatabaseType(databaseType)) return null;

                return provider.GetRequiredService<PackageDatabase>();
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

        /// <summary>
        /// Runs through all providers to resolve the <typeparamref name="TService"/>.
        /// </summary>
        /// <typeparam name="TService">The service that will be resolved using providers.</typeparam>
        /// <param name="services">The dependency injection container.</param>
        /// <returns>An instance of the service created by the providers.</returns>
        public static TService GetServiceFromProviders<TService>(IServiceProvider services)
            where TService : class
        {
            // Run through all the providers for the type. Find the first provider that results a non-null result.
            var providers = services.GetRequiredService<IEnumerable<IProvider<TService>>>();
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
