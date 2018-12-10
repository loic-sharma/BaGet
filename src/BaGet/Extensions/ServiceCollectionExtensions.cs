using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http;
using System.Reflection;
using BaGet.Azure.Configuration;
using BaGet.Azure.Extensions;
using BaGet.Azure.Search;
using BaGet.Configurations;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Core.Mirror;
using BaGet.Core.Services;
using BaGet.Entities;
using BaGet.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            services.ConfigureAndValidate<BaGetOptions>(configuration);
            services.ConfigureAndValidateSection<SearchOptions>(configuration, nameof(BaGetOptions.Search));
            services.ConfigureAndValidateSection<MirrorOptions>(configuration, nameof(BaGetOptions.Mirror));
            services.ConfigureAndValidateSection<StorageOptions>(configuration, nameof(BaGetOptions.Storage));
            services.ConfigureAndValidateSection<DatabaseOptions>(configuration, nameof(BaGetOptions.Database));
            services.ConfigureAndValidateSection<FileSystemStorageOptions>(configuration, nameof(BaGetOptions.Storage));
            services.ConfigureAndValidateSection<BlobStorageOptions>(configuration, nameof(BaGetOptions.Storage));
            services.ConfigureAndValidateSection<AzureSearchOptions>(configuration, nameof(BaGetOptions.Search));

            services.AddBaGetContext();
            services.ConfigureAzure(configuration);

            if (httpServices)
            {
                services.ConfigureHttpServices();
            }

            services.AddTransient<IPackageService, PackageService>();
            services.AddTransient<IPackageIndexingService, PackageIndexingService>();
            services.AddTransient<IPackageDeletionService, PackageDeletionService>();
            services.AddTransient<ISymbolIndexingService, SymbolIndexingService>();
            services.AddMirrorServices();

            services.ConfigureStorageProviders();
            services.ConfigureSearchProviders();
            services.ConfigureAuthenticationProviders();

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

        public static IServiceCollection ConfigureHttpServices(this IServiceCollection services)
        {
            services
                .AddMvc()
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

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

        public static IServiceCollection ConfigureStorageProviders(this IServiceCollection services)
        {
            services.AddTransient<FileStorageService>();
            services.AddTransient<IPackageStorageService, PackageStorageService>();
            services.AddTransient<ISymbolStorageService, SymbolStorageService>();

            services.AddBlobStorageService();

            services.AddTransient<IStorageService>(provider =>
            {
                var options = provider.GetRequiredService<IOptionsSnapshot<BaGetOptions>>();

                switch (options.Value.Storage.Type)
                {
                    case StorageType.FileSystem:
                        return provider.GetRequiredService<FileStorageService>();

                    case StorageType.AzureBlobStorage:
                        return provider.GetRequiredService<BlobStorageService>();

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported storage service: {options.Value.Storage.Type}");
                }
            });

            return services;
        }

        public static IServiceCollection ConfigureSearchProviders(this IServiceCollection services)
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

                return new ServiceIndexService(
                    options.Value.PackageSource.ToString(),
                    provider.GetRequiredService<IServiceIndexClient>());
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
            services.AddSingleton<IPackageDownloadsSource, PackageDownloadsJsonSource>();

            return services;
        }

        public static IServiceCollection ConfigureAuthenticationProviders(this IServiceCollection services)
        {
            services.AddTransient<IAuthenticationService, ApiKeyAuthenticationService>();

            return services;
        }

        public static IServiceCollection ConfigureAndValidateSection<TOptions>(
            this IServiceCollection services,
            IConfiguration config,
            string sectionName)
          where TOptions : class
        {
            services.ConfigureAndValidate<TOptions>(config.GetSection(sectionName), sectionName);

            return services;
        }

        public static IServiceCollection ConfigureAndValidate<TOptions>(
            this IServiceCollection services,
            IConfiguration config,
            string name = null)
          where TOptions : class
        {
            services.Configure<TOptions>(config);
            services.PostConfigure<TOptions>(options =>
            {
                var context = new ValidationContext(options);
                var validationResults = new List<ValidationResult>();
                if (!Validator.TryValidateObject(options, context, validationResults, validateAllProperties: true))
                {
                    var message = (name == null)
                        ? $"Invalid options"
                        : $"Invalid '{name}' options";

                    throw new InvalidOperationException(
                        $"{message}: {string.Join('\n', validationResults)}");
                }
            });

            return services;
        }
    }
}
