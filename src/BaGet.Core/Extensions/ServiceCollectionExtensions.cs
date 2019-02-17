using BaGet.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureAndValidate<TOptions>(
            this IServiceCollection services,
            IConfiguration config)
          where TOptions : class
        {
            services.Configure<TOptions>(config);
            services.AddSingleton<IPostConfigureOptions<TOptions>, ValidatePostConfigureOptions<TOptions>>();

            return services;
        }
    }
}
