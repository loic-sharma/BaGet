using System;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using BaGet.Aws;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public static class AwsApplicationExtensions
    {
        public static BaGetApplication AddAwsS3Storage(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<S3StorageOptions>(nameof(BaGetOptions.Storage));

            app.Services.AddTransient<S3StorageService>();
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<S3StorageService>());

            app.Services.AddProvider<IStorageService>((provider, config) =>
            {
                if (!config.HasStorageType("AwsS3")) return null;

                return provider.GetRequiredService<S3StorageService>();
            });

            app.Services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<S3StorageOptions>>().Value;

                var config = new AmazonS3Config
                {
                    RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region)
                };

                if (options.UseInstanceProfile)
                {
                    var credentials = FallbackCredentialsFactory.GetCredentials();
                    return new AmazonS3Client(credentials, config);
                }

                if (!string.IsNullOrEmpty(options.AssumeRoleArn))
                {
                    var credentials = FallbackCredentialsFactory.GetCredentials();
                    var assumedCredentials = AssumeRoleAsync(
                            credentials,
                            options.AssumeRoleArn,
                            $"BaGet-Session-{Guid.NewGuid()}")
                        .GetAwaiter()
                        .GetResult();

                    return new AmazonS3Client(assumedCredentials, config);
                }

                if (!string.IsNullOrEmpty(options.AccessKey))
                {
                    return new AmazonS3Client(
                        new BasicAWSCredentials(
                            options.AccessKey,
                            options.SecretKey),
                        config);
                }

                return new AmazonS3Client(config);
            });

            return app;
        }

        public static BaGetApplication AddAwsS3Storage(this BaGetApplication app, Action<S3StorageOptions> configure)
        {
            app.AddAwsS3Storage();
            app.Services.Configure(configure);
            return app;
        }

        private static async Task<AWSCredentials> AssumeRoleAsync(
            AWSCredentials credentials,
            string roleArn,
            string roleSessionName)
        {
            var assumedCredentials = new AssumeRoleAWSCredentials(credentials, roleArn, roleSessionName);
            var immutableCredentials = await credentials.GetCredentialsAsync();

            if (string.IsNullOrWhiteSpace(immutableCredentials.Token))
            {
                throw new InvalidOperationException($"Unable to assume role {roleArn}");
            }

            return assumedCredentials;
        }
    }
}
