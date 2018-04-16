using BaGet.Azure.Configuration;
using BaGet.Azure.Search;
using BaGet.Core.Configuration;
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
            services.Configure<BlobPackageStorageService>(configuration.GetSection(nameof(BaGetOptions.Storage)));
            services.Configure<AzureSearchOptions>(configuration.GetSection(nameof(BaGetOptions.Search)));
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

            services.AddSingleton<AzureSearchService>();
        }
    }
}
