using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core
{
    public static partial class DependencyInjectionExtensions
    {
        /// <summary>
        /// Add a new provider to the dependency injection container. The provider may
        /// provide an implementation of the service, or it may return null.
        /// </summary>
        /// <typeparam name="TService">The service that may be provided.</typeparam>
        /// <param name="services">The dependency injection container.</param>
        /// <param name="func">A handler that provides the service, or null.</param>
        /// <returns>The dependency injection container.</returns>
        public static IServiceCollection AddProvider<TService>(
            this IServiceCollection services,
            Func<IServiceProvider, IConfiguration, TService> func)
            where TService : class
        {
            services.AddSingleton<IProvider<TService>>(new DelegateProvider<TService>(func));

            return services;
        }

        /// <summary>
        /// Runs through all providers to resolve the <typeparamref name="TService"/>.
        /// </summary>
        /// <typeparam name="TService">The service that will be resolved using providers.</typeparam>
        /// <param name="services">The dependency injection container.</param>
        /// <returns>An instance of the service created by the providers.</returns>
        public static TService GetServiceFromProviders<TService>(IServiceProvider services)
            where TService : class
        {
            // Run through all the providers for the type. Find the first provider that results a non-null result.
            var providers = services.GetRequiredService<IEnumerable<IProvider<TService>>>();
            var configuration = services.GetRequiredService<IConfiguration>();

            foreach (var provider in providers)
            {
                var result = provider.GetOrNull(services, configuration);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
