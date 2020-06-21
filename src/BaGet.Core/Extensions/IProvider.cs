using System;
using Microsoft.Extensions.Configuration;

namespace BaGet.Core
{
    /// <summary>
    /// Attempts to provide the <typeparamref name="TService"/>.
    /// </summary>
    /// <typeparam name="TService">The service that may be provided.</typeparam>
    public interface IProvider<TService>
    {
        /// <summary>
        /// Attempt to provide the <typeparamref name="TService"/>.
        /// </summary>
        /// <param name="provider">The dependency injection container.</param>
        /// <param name="configuration">The app's configuration.</param>
        /// <returns>
        /// An instance of <typeparamref name="TService"/>, or, null if the
        /// provider is not currently active in the <paramref name="configuration"/>.
        /// </returns>
        TService GetOrNull(IServiceProvider provider, IConfiguration configuration);
    }

    /// <summary>
    /// Implements <see cref="IProvider{TService}"/> as a delegate.
    /// </summary>
    internal class DelegateProvider<TService> : IProvider<TService>
    {
        private readonly Func<IServiceProvider, IConfiguration, TService> _func;

        /// <summary>
        /// Create an <see cref="IProvider{TService}"/> using a delegate.
        /// </summary>
        /// <param name="func">
        /// A delegate that returns an instance of <typeparamref name="TService"/>, or,
        /// null if the provider is not currently active due to the app's configuration.</param>
        public DelegateProvider(Func<IServiceProvider, IConfiguration, TService> func)
        {
            _func = func ?? throw new ArgumentNullException(nameof(func));
        }

        public TService GetOrNull(IServiceProvider provider, IConfiguration configuration)
        {
            return _func(provider, configuration);
        }
    }
}
