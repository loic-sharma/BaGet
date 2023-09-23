using System;
using Microsoft.Extensions.DependencyInjection;

namespace BaGetter.Core
{
    public class BaGetterApplication
    {
        public BaGetterApplication(IServiceCollection services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public IServiceCollection Services { get; }
    }
}
