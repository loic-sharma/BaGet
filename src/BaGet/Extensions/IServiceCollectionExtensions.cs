using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using BaGet.Authentication;
using BaGet.AWS;
using BaGet.AWS.Configuration;
using BaGet.AWS.Extensions;
using BaGet.Azure.Configuration;
using BaGet.Azure.Extensions;
using BaGet.Azure.Search;
using BaGet.Core.Authentication;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using BaGet.Core.Mirror;
using BaGet.Core.Server.Extensions;
using BaGet.Core.Services;
using BaGet.Entities;
using BaGet.Protocol;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
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
            services.ConfigureAndValidate<FeedAuthenticationOptions>(configuration.GetSection(nameof(BaGetOptions.FeedAuthentication)));

            services.ConfigureAzure(configuration);
            services.ConfigureAws(configuration);

            if (httpServices)
            {
                services.ConfigureHttpServices();
            }

            services.AddBaGetContext();

            services.AddTransient<IPackageService, PackageService>();
            services.AddTransient<IPackageIndexingService, PackageIndexingService>();
            services.AddTransient<IPackageDeletionService, PackageDeletionService>();
            services.AddTransient<ISymbolIndexingService, SymbolIndexingService>();

            services.AddMirrorServices();

            services.AddStorageProviders();
            services.AddSearchProviders();

            services.AddAuthenticationProviders(); //API-Key

            services.ConfigureFeedAuthentication(configuration);
            services.AddSingleton<IConfigureOptions<MvcOptions>, ConfigureMvcOptions>();

            return services;
        }

        public static void ConfigureFeedAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var options = services.BuildServiceProvider().GetRequiredService<IOptionsSnapshot<FeedAuthenticationOptions>>();
            switch (options.Value.Type)
            {
                case AuthenticationType.None:
                    break;
                case AuthenticationType.AzureActiveDirectory:
                    ConfigureAzureAdAuthentication(services, configuration, options.Value.SettingsKey);
                    break;
                case AuthenticationType.Basic: 
                    ConfigureBasicAuthentication(services, configuration, options.Value.SettingsKey);
                    break;
                default:
                    throw new NotImplementedException(string.Format("AuthenticationType '{0}' not implemented yet", options.Value.Type));
            }
        }

        /// <summary>
        /// really simple scenario, basically for Testing the Infrastructure
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <param name="bindingKey"></param>
        public static void ConfigureBasicAuthentication(this IServiceCollection services, IConfiguration configuration, string bindingKey)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            if (string.IsNullOrEmpty(bindingKey)) throw new ArgumentNullException(nameof(bindingKey));
            services.AddAuthentication(sharedOptions =>
            {
                sharedOptions.DefaultScheme = BasicAuthenticationDefaults.AuthenticationScheme;
            })
            .AddBasic(BasicAuthenticationDefaults.AuthenticationScheme, options => configuration.Bind(bindingKey, options));
        }



        public static void ConfigureAzureAdAuthentication(this IServiceCollection services, IConfiguration configuration, string bindingKey)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            if (string.IsNullOrEmpty(bindingKey)) throw new ArgumentNullException(nameof(bindingKey));

            services.AddAuthentication(sharedOptions =>
            {
                sharedOptions.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddAzureAdBearer(options => configuration.Bind(bindingKey, options));
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

        public static IServiceCollection AddStorageProviders(this IServiceCollection services)
        {
            services.AddTransient<FileStorageService>();
            services.AddTransient<IPackageStorageService, PackageStorageService>();
            services.AddTransient<ISymbolStorageService, SymbolStorageService>();

            services.AddBlobStorageService();
            services.AddS3StorageService();

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
                var options = provider.GetRequiredService<IOptionsSnapshot<SearchOptions>>();

                switch (options.Value.Type)
                {
                    case SearchType.Database:
                        return provider.GetRequiredService<DatabaseSearchService>();

                    case SearchType.Azure:
                        return provider.GetRequiredService<AzureSearchService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported search service: {options.Value.Type}");
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
            services.AddTransient<FakeMirrorService>();
            services.AddTransient<MirrorService>();

            services.AddTransient<IMirrorService>(provider =>
            {
                var options = provider.GetRequiredService<IOptionsSnapshot<MirrorOptions>>();

                if (!options.Value.Enabled)
                {
                    return provider.GetRequiredService<FakeMirrorService>();
                }
                else
                {
                    return provider.GetRequiredService<MirrorService>();
                }
            });

            services.AddTransient<IPackageContentClient, PackageContentClient>();
            services.AddTransient<IRegistrationClient, RegistrationClient>();
            services.AddTransient<IServiceIndexClient, ServiceIndexClient>();
            services.AddTransient<IPackageMetadataService, PackageMetadataService>();

            services.AddSingleton<IServiceIndexService>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<MirrorOptions>>();
                var serviceIndexClient = provider.GetRequiredService<IServiceIndexClient>();

                return new ServiceIndexService(
                    options.Value.PackageSource.ToString(),
                    serviceIndexClient);
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
            services.AddTransient<BaGet.Core.Services.IAuthenticationService, ApiKeyAuthenticationService>();
            return services;
        }
    }
}
