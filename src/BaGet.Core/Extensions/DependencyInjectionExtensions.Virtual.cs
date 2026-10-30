using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGet.Core;

public static partial class DependencyInjectionExtensions
{
    /// <summary>
    /// Makes the registered <see cref="ISearchService"/> repository-aware: a virtual repository is searched through its
    /// members (<see cref="RepositorySearchService"/>), any other repository by the registered service as before. The
    /// last registration is the one wrapped, so a host that registers its search service after
    /// <c>AddBaGetApplication</c> calls this again once it has done so; wrapping twice is harmless (an already wrapped
    /// registration is left alone).
    /// </summary>
    public static IServiceCollection AddRepositoryAwareSearch(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        var original = services.LastOrDefault(d => d.ServiceType == typeof(ISearchService) && !d.IsKeyedService);
        if (original == null || original.ImplementationFactory?.Target is RepositorySearchFactory) return services;

        var lifetime = original.Lifetime == ServiceLifetime.Singleton ? ServiceLifetime.Scoped : original.Lifetime;
        services.Add(new ServiceDescriptor(typeof(ISearchService), new RepositorySearchFactory(original).Create, lifetime));

        return services;
    }

    private static void AddVirtualRepositoryServices(this IServiceCollection services)
    {
        services.TryAddScoped<IRequestCaller, RequestCaller>();
        services.TryAddScoped<IVirtualRepositories, VirtualRepositories>();
        services.TryAddScoped<VirtualRepositoryReader>();
        services.TryAddScoped<IVirtualRepositoryAdministration, VirtualRepositoryAdministration>();

        // The virtual-aware services wrap the concrete implementations, which stay registered on their own so a
        // member's scope can ask them directly.
        services.TryAddTransient<DefaultPackageContentService>();
        services.TryAddTransient<SymbolStorageService>();
    }

    private sealed class RepositorySearchFactory
    {
        private readonly ServiceDescriptor _original;

        public RepositorySearchFactory(ServiceDescriptor original)
        {
            _original = original;
        }

        public ISearchService Create(IServiceProvider provider)
        {
            var inner = _original.ImplementationInstance as ISearchService
                ?? (_original.ImplementationFactory != null
                    ? (ISearchService)_original.ImplementationFactory(provider)
                    : (ISearchService)ActivatorUtilities.CreateInstance(provider, _original.ImplementationType!));

            return new RepositorySearchService(
                inner,
                provider.GetRequiredService<VirtualRepositoryReader>(),
                provider.GetRequiredService<ISearchResponseBuilder>());
        }
    }
}
