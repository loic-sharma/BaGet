using BaGet.Azure.Configuration;
using BaGet.Azure.Search;
using BaGet.Core.Services;
using Microsoft.Azure.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BaGet.Azure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBlobStorageService(this IServiceCollection services)
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

                return client.GetContainerReference(options.Container);
            });

            services.AddTransient<BlobStorageService>();

            return services;
        }

        public static IServiceCollection AddAzureSearch(this IServiceCollection services)
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

            return services;
        }
    }
}
