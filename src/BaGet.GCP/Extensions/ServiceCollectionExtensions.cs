using BaGet.GCP.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.GCP.Extensions
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
