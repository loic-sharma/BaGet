using System;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGet
{
    public static class BaGetApplicationExtensions
    {
        public static BaGetApplication AddFileStorage(this BaGetApplication app)
        {
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<FileStorageService>());
            return app;
        }

        public static BaGetApplication AddFileStorage(
            this BaGetApplication app,
            Action<FileSystemStorageOptions> configure)
        {
            app.AddFileStorage();
            app.Services.Configure(configure);
            return app;
        }

        public static BaGetApplication AddNullStorage(this BaGetApplication app)
        {
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<NullStorageService>());
            return app;
        }

        public static BaGetApplication AddNullSearch(this BaGetApplication app)
        {
            app.Services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
            app.Services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<NullSearchService>());
            return app;
        }
    }
}
