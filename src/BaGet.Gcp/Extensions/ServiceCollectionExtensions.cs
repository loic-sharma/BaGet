using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Gcp
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
