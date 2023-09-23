using System;
using BaGetter.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGetter
{
    public static class BaGetterApplicationExtensions
    {
        public static BaGetterApplication AddFileStorage(this BaGetterApplication app)
        {
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<FileStorageService>());
            return app;
        }

        public static BaGetterApplication AddFileStorage(
            this BaGetterApplication app,
            Action<FileSystemStorageOptions> configure)
        {
            app.AddFileStorage();
            app.Services.Configure(configure);
            return app;
        }

        public static BaGetterApplication AddNullStorage(this BaGetterApplication app)
        {
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<NullStorageService>());
            return app;
        }

        public static BaGetterApplication AddNullSearch(this BaGetterApplication app)
        {
            app.Services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
            app.Services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<NullSearchService>());
            return app;
        }
    }
}
