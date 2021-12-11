using System;
using BaGet.Azure;
using BaGet.Core;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BaGet
{
    using CloudStorageAccount = Microsoft.WindowsAzure.Storage.CloudStorageAccount;
    using StorageCredentials = Microsoft.WindowsAzure.Storage.Auth.StorageCredentials;

    using TableStorageAccount = Microsoft.Azure.Cosmos.Table.CloudStorageAccount;

    public static class AzureApplicationExtensions
    {
        public static BaGetApplication AddAzureTableDatabase(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<AzureTableOptions>(nameof(BaGetOptions.Database));

            app.Services.AddTransient<TablePackageDatabase>();
            app.Services.AddTransient<TableOperationBuilder>();
            app.Services.AddTransient<TableSearchService>();
            app.Services.TryAddTransient<IPackageDatabase>(provider => provider.GetRequiredService<TablePackageDatabase>());
            app.Services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<TableSearchService>());
            app.Services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());

            app.Services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AzureTableOptions>>().Value;

                return TableStorageAccount.Parse(options.ConnectionString);
            });

            app.Services.AddTransient(provider =>
            {
                var account = provider.GetRequiredService<TableStorageAccount>();

                return account.CreateCloudTableClient();
            });

            app.Services.AddProvider<IPackageDatabase>((provider, config) =>
            {
                if (!config.HasDatabaseType("AzureTable")) return null;

                return provider.GetRequiredService<TablePackageDatabase>();
            });

            app.Services.AddProvider<ISearchService>((provider, config) =>
            {
                if (!config.HasSearchType("Database")) return null;
                if (!config.HasDatabaseType("AzureTable")) return null;

                return provider.GetRequiredService<TableSearchService>();
            });

            app.Services.AddProvider<ISearchIndexer>((provider, config) =>
            {
                if (!config.HasSearchType("Database")) return null;
                if (!config.HasDatabaseType("AzureTable")) return null;

                return provider.GetRequiredService<NullSearchIndexer>();
            });

            return app;
        }

        public static BaGetApplication AddAzureTableDatabase(
            this BaGetApplication app,
            Action<AzureTableOptions> configure)
        {
            app.AddAzureTableDatabase();
            app.Services.Configure(configure);
            return app;
        }

        public static BaGetApplication AddAzureBlobStorage(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<AzureBlobStorageOptions>(nameof(BaGetOptions.Storage));
            app.Services.AddTransient<BlobStorageService>();
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<BlobStorageService>());

            app.Services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AzureBlobStorageOptions>>().Value;

                if (!string.IsNullOrEmpty(options.ConnectionString))
                {
                    return CloudStorageAccount.Parse(options.ConnectionString);
                }

                return new CloudStorageAccount(
                    new StorageCredentials(
                        options.AccountName,
                        options.AccessKey),
                    useHttps: true);
            });

            app.Services.AddTransient(provider =>
            {
                var options = provider.GetRequiredService<IOptionsSnapshot<AzureBlobStorageOptions>>().Value;
                var account = provider.GetRequiredService<CloudStorageAccount>();

                var client = account.CreateCloudBlobClient();

                return client.GetContainerReference(options.Container);
            });

            app.Services.AddProvider<IStorageService>((provider, config) =>
            {
                if (!config.HasStorageType("AzureBlobStorage")) return null;

                return provider.GetRequiredService<BlobStorageService>();
            });

            return app;
        }

        public static BaGetApplication AddAzureBlobStorage(
            this BaGetApplication app,
            Action<AzureBlobStorageOptions> configure)
        {
            app.AddAzureBlobStorage();
            app.Services.Configure(configure);
            return app;
        }

        public static BaGetApplication AddAzureSearch(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<AzureSearchOptions>(nameof(BaGetOptions.Search));

            app.Services.AddTransient<AzureSearchBatchIndexer>();
            app.Services.AddTransient<AzureSearchService>();
            app.Services.AddTransient<AzureSearchIndexer>();
            app.Services.AddTransient<IndexActionBuilder>();
            app.Services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<AzureSearchService>());
            app.Services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<AzureSearchIndexer>());

            app.Services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AzureSearchOptions>>().Value;
                var credentials = new SearchCredentials(options.ApiKey);

                return new SearchServiceClient(options.AccountName, credentials);
            });

            app.Services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AzureSearchOptions>>().Value;
                var credentials = new SearchCredentials(options.ApiKey);

                return new SearchIndexClient(options.AccountName, PackageDocument.IndexName, credentials);
            });

            app.Services.AddProvider<ISearchService>((provider, config) =>
            {
                if (!config.HasSearchType("AzureSearch")) return null;

                return provider.GetRequiredService<AzureSearchService>();
            });

            app.Services.AddProvider<ISearchIndexer>((provider, config) =>
            {
                if (!config.HasSearchType("AzureSearch")) return null;

                return provider.GetRequiredService<AzureSearchIndexer>();
            });

            return app;
        }

        public static BaGetApplication AddAzureSearch(
            this BaGetApplication app,
            Action<AzureSearchOptions> configure)
        {
            app.AddAzureSearch();
            app.Services.Configure(configure);
            return app;
        }
    }
}
