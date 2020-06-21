using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGet.Core
{
    public static class BaGetApplicationExtensions
    {
        public static void AddFileStorage(this BaGetApplication app)
        {
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<FileStorageService>());
        }

        public static void AddNullStorage(this BaGetApplication app)
        {
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<NullStorageService>());
        }

        public static void AddNullSearch(this BaGetApplication app)
        {
            app.Services.TryAddTransient<ISearchIndexer>(provider => provider.GetRequiredService<NullSearchIndexer>());
            app.Services.TryAddTransient<ISearchService>(provider => provider.GetRequiredService<NullSearchService>());
        }
    }
}
