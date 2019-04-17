using BaGet.Azure.Configuration;
using BaGet.Azure.Search;
using BaGet.Core.Configuration;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BaGet.Azure.Extensions
{
    using CloudStorageAccount = Microsoft.WindowsAzure.Storage.CloudStorageAccount;
    using StorageCredentials = Microsoft.WindowsAzure.Storage.Auth.StorageCredentials;

    using TableStorageAccount = Microsoft.Azure.Cosmos.Table.CloudStorageAccount;

    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddTableStorageService(this IServiceCollection services)
        {
            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

                return TableStorageAccount.Parse(options.ConnectionString);
            });

            services.AddTransient(provider =>
            {
                var account = provider.GetRequiredService<TableStorageAccount>();

                return account.CreateCloudTableClient();
            });

            services.AddTransient<TablePackageService>();

            return services;
        }

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
                var options = provider.GetRequiredService<IOptionsSnapshot<BlobStorageOptions>>().Value;
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
