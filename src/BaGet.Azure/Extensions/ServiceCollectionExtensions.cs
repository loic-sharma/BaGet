using BaGet.Azure.Authentication;
using BaGet.Azure.Configuration;
using BaGet.Azure.Search;
using BaGet.Core.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;

namespace BaGet.Azure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void ConfigureAzure(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<BlobStorageOptions>(configuration.GetSection(nameof(BaGetOptions.Storage)));
            services.Configure<AzureSearchOptions>(configuration.GetSection(nameof(BaGetOptions.Search)));
            services.Configure<AzureActiveDirectoryOptions>(configuration.GetSection(nameof(BaGetOptions.Authentication)));
        }

        public static void AddBlobPackageStorageService(this IServiceCollection services)
        {
            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value;

                return new CloudStorageAccount(
                    new StorageCredentials(
                        options.AccountName,
                        options.AccessKey),
                    useHttps: true);
            });

            services.AddTransient(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
                var account = provider.GetRequiredService<CloudStorageAccount>();

                var client = account.CreateCloudBlobClient();
                var container = client.GetContainerReference(options.Container);

                return new BlobPackageStorageService(container);
            });
        }

        public static void AddAzureSearch(this IServiceCollection services)
        {
            services.AddTransient<BatchIndexer>();
            services.AddTransient<AzureSearchService>();

            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AzureSearchOptions>>().Value;
                var credentials = new SearchCredentials(options.ApiKey);

                return new SearchServiceClient(options.AccountName, credentials);
            });

            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AzureSearchOptions>>().Value;
                var credentials = new SearchCredentials(options.ApiKey);

                return new SearchIndexClient(options.AccountName, PackageDocument.IndexName, credentials);
            });
        }

        public static void AddAzureAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            // https://github.com/Azure-Samples/active-directory-dotnet-webapp-webapi-openidconnect-aspnetcore
            // https://github.com/Azure-Samples/active-directory-dotnet-native-aspnetcore
            services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

            services
                .AddAuthentication(options => options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer();
        }
    }
}
