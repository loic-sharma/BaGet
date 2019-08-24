using BaGet.Gcp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Gcp.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGoogleCloudStorageService(this IServiceCollection services)
        {
            services.AddTransient<GoogleCloudStorageService>();
            return services;
        }
    }
}
