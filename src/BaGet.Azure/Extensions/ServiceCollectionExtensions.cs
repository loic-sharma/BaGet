using BaGet.Azure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;

namespace BaGet.Azure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddBlobPackageStorageService(this IServiceCollection services)
        {
            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<AzureOptions>().Storage;

                return new CloudStorageAccount(
                    new StorageCredentials(
                        options.AccountName,
                        options.AccessKey),
                    useHttps: true);
            });

            services.AddTransient(provider =>
            {
                var options = provider.GetRequiredService<AzureOptions>().Storage;
                var account = provider.GetRequiredService<CloudStorageAccount>();

                var client = account.CreateCloudBlobClient();
                var container = client.GetContainerReference(options.Container);

                return new BlobPackageStorageService(container);
            });
        }
    }
}
