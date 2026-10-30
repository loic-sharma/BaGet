using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BaGet.Catalog.Database;
using BaGet.Catalog.Rebuild;
using BaGet.Catalog.ServiceIndex;
using BaGet.Catalog.Storage;

namespace BaGet.Catalog.Extensions;

public static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddBaGetCatalog(this IServiceCollection services)
    {
        // A database without the catalog table, such as Azure Table storage, is
        // registered as the null context; the catalog is then switched off.
        services.TryAddScoped<ICatalogDatabase>(provider =>
            provider.GetRequiredService<IContext>() is NullContext
                ? (ICatalogDatabase)new NullCatalogDatabase()
                : new CatalogDatabase(provider.GetRequiredService<IContext>()));

        services.TryAddTransient<CatalogWriter>();
        services.TryAddTransient<CatalogReader>();
        services.TryAddTransient<CatalogRebuilder>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdminOperation, CatalogRebuildOperation>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPackageChangeHandler, CatalogWriter>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IServiceIndexResourceProvider, CatalogServiceIndexProvider>());

        return services;
    }
}