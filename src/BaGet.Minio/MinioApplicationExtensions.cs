using System;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Minio;

namespace BaGet.Minio
{
    public static class MinioApplicationExtensions
    {
        public static BaGetApplication AddMinioStorage(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<MinioStorageOptions>(nameof(BaGetOptions.Storage));

            app.Services.AddTransient<MinioStorageService>();
            app.Services.TryAddTransient<IStorageService>(
                provider => provider.GetRequiredService<MinioStorageService>());

            app.Services.AddProvider<IStorageService>((provider, config) =>
                !config.HasStorageType("Minio") ? null : provider.GetRequiredService<MinioStorageService>());

            app.Services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<MinioStorageOptions>>().Value;

                var client = new MinioClient(options.Endpoint, options.AccessKey, options.SecretKey,
                    options.Region ?? string.Empty);

                if (options.Secure) client.WithSSL();

                return client;
            });

            return app;
        }

        public static BaGetApplication AddMinioStorage(this BaGetApplication app, Action<MinioStorageOptions> configure)
        {
            app.AddMinioStorage();
            app.Services.Configure(configure);
            return app;
        }
    }
}
