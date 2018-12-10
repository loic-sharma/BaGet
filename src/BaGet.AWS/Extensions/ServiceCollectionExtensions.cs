using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using BaGet.AWS.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.AWS.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddS3StorageService(this IServiceCollection services)
        {
            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

                AmazonS3Config config = new AmazonS3Config
                {
                    RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region)
                };

                if (!string.IsNullOrEmpty(options.AccessKey))
                    return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);

                return new AmazonS3Client(config);
            });

            services.AddTransient<S3StorageService>();

            return services;
        }
    }
}
