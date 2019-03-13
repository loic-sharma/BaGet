using BaGet.Core.Services;
using BaGet.GCP.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.GCP.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGoogleBucketStorageService(this IServiceCollection services)
        {
            services.AddTransient<GoogleBucketStorageService>();
            return services;
        }
    }
}
