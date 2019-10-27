using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using BaGet.Aws;
using BaGet.Aws.Configuration;
using BaGet.Aws.Extensions;
using BaGet.Azure;
using BaGet.Core;
using BaGet.Core.Content;
using BaGet.Core.Server.Extensions;
using BaGet.Database.MySql;
using BaGet.Database.PostgreSql;
using BaGet.Database.Sqlite;
using BaGet.Database.SqlServer;
using BaGet.Gcp.Configuration;
using BaGet.Gcp.Extensions;
using BaGet.Gcp.Services;
using BaGet.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Extensions
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureBaGet(
            this IServiceCollection services,
            IConfiguration configuration,
            bool httpServices = false)
        {
            services.ConfigureAndValidate<BaGetOptions>(configuration);
            services.ConfigureAndValidate<SearchOptions>(configuration.GetSection(nameof(BaGetOptions.Search)));
            services.ConfigureAndValidate<MirrorOptions>(configuration.GetSection(nameof(BaGetOptions.Mirror)));
            services.ConfigureAndValidate<StorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));
            services.ConfigureAndValidate<DatabaseOptions>(configuration.GetSection(nameof(BaGetOptions.Database)));
            services.ConfigureAndValidate<FileSystemStorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));
            services.ConfigureAndValidate<BlobStorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));
            services.ConfigureAndValidate<AzureSearchOptions>(configuration.GetSection(nameof(BaGetOptions.Search)));

            services.ConfigureAzure(configuration);
            services.ConfigureAws(configuration);
            services.ConfigureGcp(configuration);

            if (httpServices)
            {
                services.ConfigureHttpServices();
            }

            services.AddBaGetContext();

            services.AddTransient<IUrlGenerator, BaGetUrlGenerator>();

            services.AddTransient<IPackageService>(provider =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                switch (databaseOptions.Value.Type)
                {
                    case DatabaseType.Sqlite:
                    case DatabaseType.SqlServer:
                    case DatabaseType.MySql:
                    case DatabaseType.PostgreSql:
                        return new PackageService(provider.GetRequiredService<IContext>());

                    case DatabaseType.AzureTable:
                        return provider.GetRequiredService<TablePackageService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported database provider: {databaseOptions.Value.Type}");
                }
            });

            services.AddTransient<PackageIndexingService>();
            services.AddTransient<PackageDeletionService>();
            services.AddTransient<SymbolIndexingService>();
            services.AddTransient<ServiceIndex>();
            services.AddTransient<PackageContentService>();
            services.AddTransient<PackageMetadataService>();
            services.AddSingleton<FrameworkCompatibilityService>();
            services.AddSingleton<RegistrationBuilder>();
            services.AddMirrorServices();

            services.AddStorageProviders();
            services.AddSearchProviders();
            services.AddAuthenticationProviders();

            return services;
        }

        public static IServiceCollection AddBaGetContext(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.AddScoped<IContext>(provider =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                switch (databaseOptions.Value.Type)
                {
                    case DatabaseType.Sqlite:
                        return provider.GetRequiredService<SqliteContext>();

                    case DatabaseType.SqlServer:
                        return provider.GetRequiredService<SqlServerContext>();

                    case DatabaseType.MySql:
                        return provider.GetRequiredService<MySqlContext>();

                    case DatabaseType.PostgreSql:
                        return provider.GetRequiredService<PostgreSqlContext>();

                    case DatabaseType.AzureTable:
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported database provider: {databaseOptions.Value.Type}");
                }
            });

            services.AddDbContext<SqliteContext>((provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                options.UseSqlite(databaseOptions.Value.ConnectionString);
            });

            services.AddDbContext<SqlServerContext>((provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                options.UseSqlServer(databaseOptions.Value.ConnectionString);
            });

            services.AddDbContext<MySqlContext>((provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                options.UseMySql(databaseOptions.Value.ConnectionString);
            });

            services.AddDbContext<PostgreSqlContext>((provider, options) =>
            {
                var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                options.UseNpgsql(databaseOptions.Value.ConnectionString);
            });

            return services;
        }

        public static IServiceCollection ConfigureAzure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.ConfigureAndValidate<BlobStorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));
            services.ConfigureAndValidate<AzureSearchOptions>(configuration.GetSection(nameof(BaGetOptions.Search)));

            return services;
        }

        public static IServiceCollection ConfigureAws(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.ConfigureAndValidate<S3StorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));

            return services;
        }

        public static IServiceCollection ConfigureGcp(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.ConfigureAndValidate<GoogleCloudStorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));

            return services;
        }

        public static IServiceCollection AddStorageProviders(this IServiceCollection services)
        {
            services.AddSingleton<NullStorageService>();
            services.AddTransient<FileStorageService>();
            services.AddTransient<PackageStorageService>();
            services.AddTransient<SymbolStorageService>();

            services.AddTableStorageService();
            services.AddBlobStorageService();
            services.AddS3StorageService();
            services.AddGoogleCloudStorageService();

            services.AddTransient<IStorageService>(provider =>
            {
                var options = provider.GetRequiredService<IOptionsSnapshot<BaGetOptions>>();

                switch (options.Value.Storage.Type)
                {
                    case StorageType.FileSystem:
                        return provider.GetRequiredService<FileStorageService>();

                    case StorageType.AzureBlobStorage:
                        return provider.GetRequiredService<BlobStorageService>();

                    case StorageType.AwsS3:
                        return provider.GetRequiredService<S3StorageService>();

                    case StorageType.GoogleCloud:
                        return provider.GetRequiredService<GoogleCloudStorageService>();

                    case StorageType.Null:
                        return provider.GetRequiredService<NullStorageService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported storage service: {options.Value.Storage.Type}");
                }
            });

            return services;
        }

        public static IServiceCollection AddSearchProviders(this IServiceCollection services)
        {
            services.AddTransient<ISearchService>(provider =>
            {
                var searchOptions = provider.GetRequiredService<IOptionsSnapshot<SearchOptions>>();

                switch (searchOptions.Value.Type)
                {
                    case SearchType.Database:
                        var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();

                        switch (databaseOptions.Value.Type)
                        {
                            case DatabaseType.MySql:
                            case DatabaseType.PostgreSql:
                            case DatabaseType.Sqlite:
                            case DatabaseType.SqlServer:
                                return provider.GetRequiredService<DatabaseSearchService>();

                            case DatabaseType.AzureTable:
                                return provider.GetRequiredService<TableSearchService>();

                            default:
                                throw new InvalidOperationException(
                                    $"Database type '{databaseOptions.Value.Type}' cannot be used with " +
                                    $"search type '{searchOptions.Value.Type}'");
                        }

                    case SearchType.Azure:
                        return provider.GetRequiredService<AzureSearchService>();

                    case SearchType.Null:
                        return provider.GetRequiredService<NullSearchService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported search service: {searchOptions.Value.Type}");
                }
            });

            services.AddTransient<ISearchIndexer>(provider =>
            {
                var searchOptions = provider.GetRequiredService<IOptionsSnapshot<SearchOptions>>();

                switch (searchOptions.Value.Type)
                {
                    case SearchType.Null:
                    case SearchType.Database:
                        return provider.GetRequiredService<NullSearchIndexer>();

                    case SearchType.Azure:
                        return provider.GetRequiredService<AzureSearchIndexer>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported search service: {searchOptions.Value.Type}");
                }
            });

            services.AddTransient<DatabaseSearchService>();
            services.AddSingleton<NullSearchService>();
            services.AddSingleton<NullSearchIndexer>();
            services.AddAzureSearch();
            services.AddAzureTableSearch();

            return services;
        }

        /// <summary>
        /// Add the services that mirror an upstream package source.
        /// </summary>
        /// <param name="services">The defined services.</param>
        public static IServiceCollection AddMirrorServices(this IServiceCollection services)
        {
            services.AddTransient<NullMirrorService>();
            services.AddTransient<MirrorService>();

            services.AddTransient<IMirrorService>(provider =>
            {
                var options = provider.GetRequiredService<IOptionsSnapshot<MirrorOptions>>();

                if (!options.Value.Enabled)
                {
                    return provider.GetRequiredService<NullMirrorService>();
                }
                else
                {
                    return provider.GetRequiredService<MirrorService>();
                }
            });

            services.AddSingleton<NuGetClient>();
            services.AddSingleton(provider =>
            {
                var httpClient = provider.GetRequiredService<HttpClient>();
                var options = provider.GetRequiredService<IOptions<MirrorOptions>>();

                return new NuGetClientFactory(
                    httpClient,
                    options.Value.PackageSource.ToString());
            });

            services.AddSingleton(provider =>
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

            services.AddSingleton<DownloadsImporter>();
            services.AddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

            return services;
        }

        public static IServiceCollection AddAuthenticationProviders(this IServiceCollection services)
        {
            services.AddTransient<IAuthenticationService, ApiKeyAuthenticationService>();

            return services;
        }
    }
}
