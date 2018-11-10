using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using BaGet.Azure.Configuration;
using BaGet.Azure.Extensions;
using BaGet.Azure.Search;
using BaGet.Configurations;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using BaGet.Core.Mirror;
using BaGet.Core.Services;
using BaGet.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureBaGet(
            this IServiceCollection services,
            IConfiguration configuration,
            bool httpServices = false)
        {
            services.Configure<BaGetOptions>(configuration);

            services.AddBaGetContext();
            services.ConfigureAzure(configuration);

            if (httpServices)
            {
                services.ConfigureHttpServices();
            }

            services.AddTransient<IPackageService, PackageService>();
            services.AddTransient<IIndexingService, IndexingService>();
            services.AddTransient<IPackageDeletionService, PackageDeletionService>();
            services.AddMirrorServices();

            services.ConfigureStorageProviders(configuration);
            services.ConfigureSearchProviders();
            services.ConfigureAuthenticationProviders();

            return services;
        }

        public static IServiceCollection AddBaGetContext(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.AddScoped<IContext>(provider =>
            {
                var databaseOptions = provider.GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Database;

                databaseOptions.EnsureValid();

                switch (databaseOptions.Type)
                {
                    case DatabaseType.Sqlite:
                        return provider.GetRequiredService<SqliteContext>();

                    case DatabaseType.SqlServer:
                        return provider.GetRequiredService<SqlServerContext>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported database provider: {databaseOptions.Type}");
                }
            });

            services.AddDbContext<SqliteContext>((provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Database;

                options.UseSqlite(databaseOptions.ConnectionString);
            });

            services.AddDbContext<SqlServerContext>((provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Database;

                options.UseSqlServer(databaseOptions.ConnectionString);
            });

            return services;
        }

        public static IServiceCollection ConfigureHttpServices(this IServiceCollection services)
        {
            services.AddMvc();
            services.AddCors();
            services.AddSingleton<IConfigureOptions<CorsOptions>, ConfigureCorsOptions>();

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // Do not restrict to local network/proxy
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            return services;
        }

        public static IServiceCollection ConfigureStorageProviders(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<FileSystemStorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));

            services.AddTransient<IPackageStorageService>(provider =>
            {
                var storageOptions = provider
                    .GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Storage;

                storageOptions.EnsureValid();

                switch (storageOptions.Type)
                {
                    case StorageType.FileSystem:
                        return provider.GetRequiredService<FilePackageStorageService>();

                    case StorageType.AzureBlobStorage:
                        return provider.GetRequiredService<BlobPackageStorageService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported storage service: {storageOptions.Type}");
                }
            });

            services.AddTransient(provider =>
            {
                var options = provider
                    .GetRequiredService<IOptions<FileSystemStorageOptions>>()
                    .Value;

                options.EnsureValid();

                return new FilePackageStorageService(options.Path);
            });

            services.AddBlobPackageStorageService();

            return services;
        }

        public static IServiceCollection ConfigureSearchProviders(this IServiceCollection services)
        {
            services.AddTransient<ISearchService>(provider =>
            {
                var searchOptions = provider
                    .GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Search;

                searchOptions.EnsureValid();

                switch (searchOptions.Type)
                {
                    case SearchType.Database:
                        return provider.GetRequiredService<DatabaseSearchService>();

                    case SearchType.Azure:
                        return provider.GetRequiredService<AzureSearchService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported search service: {searchOptions.Type}");
                }
            });

            services.AddTransient<DatabaseSearchService>();
            services.AddAzureSearch();

            return services;
        }

        /// <summary>
        /// Add the services that mirror an upstream package source.
        /// </summary>
        /// <param name="services">The defined services.</param>
        public static IServiceCollection AddMirrorServices(this IServiceCollection services)
        {
            services.AddTransient<IMirrorService>(provider =>
            {
                var mirrorOptions = provider
                    .GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Mirror;

                mirrorOptions.EnsureValid();

                if (!mirrorOptions.Enabled)
                {
                    return new FakeMirrorService();
                }

                return new MirrorService(
                    mirrorOptions.PackageSource,
                    provider.GetRequiredService<IPackageService>(),
                    provider.GetRequiredService<IPackageDownloader>(),
                    provider.GetRequiredService<IIndexingService>(),
                    provider.GetRequiredService<ILogger<MirrorService>>());
            });

            services.AddTransient<IPackageDownloader, PackageDownloader>();

            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                var assembly = Assembly.GetEntryAssembly();
                var assemblyName = assembly.GetName().Name;
                var assemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

                var client = new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
                });

                client.DefaultRequestHeaders.Add("User-Agent", $"{assemblyName}/{assemblyVersion}");
                client.Timeout = TimeSpan.FromSeconds(options.Mirror.PackageDownloadTimeoutSeconds);

                return client;
            });

            services.AddSingleton<DownloadsImporter>();

            services.AddSingleton<IPackageDownloadsSource>(provider =>
            {
                return new PackageDownloadsJsonSource(
                    new HttpClient(),
                    provider.GetRequiredService<ILogger<PackageDownloadsJsonSource>>());
            });

            return services;
        }

        public static IServiceCollection ConfigureAuthenticationProviders(this IServiceCollection services)
        {
            services.AddSingleton<IAuthenticationService, ApiKeyAuthenticationService>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                return new ApiKeyAuthenticationService(options.ApiKeyHash);
            });

            return services;
        }
    }
}
