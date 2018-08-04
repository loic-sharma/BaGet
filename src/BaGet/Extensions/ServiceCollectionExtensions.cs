using System;
using System.IO;
using System.Net;
using System.Net.Http;
using BaGet.Azure.Configuration;
using BaGet.Azure.Extensions;
using BaGet.Azure.Search;
using BaGet.Configurations;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
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
        public static void AddBaGetContext(this IServiceCollection services)
        {
            services.AddScoped<IContext>(provider =>
            {
                var databaseOptions = provider.GetRequiredService<IOptions<BaGetOptions>>()
                    .Value
                    .Database;

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
        }

        public static void ConfigureHttpServices(this IServiceCollection services)
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
        }

        public static void ConfigureStorageProviders(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<FileSystemStorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));

            services.AddTransient<IPackageStorageService>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                switch (options.Storage.Type)
                {
                    case StorageType.FileSystem:
                        return provider.GetRequiredService<FilePackageStorageService>();

                    case StorageType.AzureBlobStorage:
                        return provider.GetRequiredService<BlobPackageStorageService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported storage service: {options.Storage.Type}");
                }
            });

            services.AddTransient(provider =>
            {
                var options = provider.GetRequiredService<IOptions<FileSystemStorageOptions>>().Value;
                var path = string.IsNullOrEmpty(options.Path)
                    ? Path.Combine(Directory.GetCurrentDirectory(), "Packages")
                    : options.Path;

                // Ensure the package storage directory exists
                Directory.CreateDirectory(path);

                return new FilePackageStorageService(path);
            });

            services.AddBlobPackageStorageService();
        }

        public static void ConfigureSearchProviders(this IServiceCollection services)
        {
            services.AddTransient<ISearchService>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                switch (options.Search.Type)
                {
                    case SearchType.Database:
                        return provider.GetRequiredService<DatabaseSearchService>();

                    case SearchType.Azure:
                        return provider.GetRequiredService<AzureSearchService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported search service: {options.Search.Type}");
                }
            });

            services.AddTransient<DatabaseSearchService>();
            services.AddAzureSearch();
        }

        /// <summary>
        /// Add the services that mirror an upstream package source.
        /// </summary>
        /// <param name="services">The defined services.</param>
        public static void AddMirrorServices(this IServiceCollection services)
        {
            services.AddTransient<IMirrorService>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                if (!options.Mirror.Enabled)
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

        public static void ConfigureAuthenticationProviders(this IServiceCollection services)
        {
            services.AddSingleton<IAuthenticationService, ApiKeyAuthenticationService>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BaGetOptions>>().Value;

                return new ApiKeyAuthenticationService(options.ApiKeyHash);
            });
        }
    }
}
