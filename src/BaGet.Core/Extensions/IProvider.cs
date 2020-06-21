using System;
using Microsoft.Extensions.Configuration;

namespace BaGet.Core
{
    public interface IProvider<T>
    {
        T GetOrNull(IServiceProvider provider, IConfiguration configuration);
    }

    internal class DelegateProvider<T> : IProvider<T>
    {
        private readonly Func<IServiceProvider, IConfiguration, T> _func;

        public DelegateProvider(Func<IServiceProvider, IConfiguration, T> func)
        {
            _func = func ?? throw new ArgumentNullException(nameof(func));
        }

        public T GetOrNull(IServiceProvider provider, IConfiguration configuration)
        {
            return _func(provider, configuration);
        }
    }
}
