using System;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using BaGet.AWS.Configuration;
using BaGet.AWS.Helpers;
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

                var config = new AmazonS3Config
                {
                    RegionEndpoint = (options.Region != null)
                        ? RegionEndpoint.GetBySystemName(options.Region)
                        : null,
                    ServiceURL = options.ServiceUrl
                };

                if (options.UseInstanceProfile)
                {
                    var credentials = FallbackCredentialsFactory.GetCredentials();
                    return new AmazonS3Client(credentials, config);
                }
                
                if (!string.IsNullOrEmpty(options.AssumeRoleArn))
                {
                    var credentials = FallbackCredentialsFactory.GetCredentials();
                    var assumedCredentials = AwsIamHelper
                        .AssumeRoleAsync(credentials, options.AssumeRoleArn, $"BaGet-Session-{Guid.NewGuid()}").GetAwaiter().GetResult();

                    return new AmazonS3Client(assumedCredentials, config);
                }
                    

                if (!string.IsNullOrEmpty(options.AccessKey))
                    return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);

                return new AmazonS3Client(config);
            });

            services.AddTransient<S3StorageService>();

            return services;
        }
    }
}
