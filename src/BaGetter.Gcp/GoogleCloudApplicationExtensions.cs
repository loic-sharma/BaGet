using System;
using BaGetter.Core;
using BaGetter.Gcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BaGetter
{
    public static class GoogleCloudApplicationExtensions
    {
        public static BaGetterApplication AddGoogleCloudStorage(this BaGetterApplication app)
        {
            app.Services.AddBaGetterOptions<GoogleCloudStorageOptions>(nameof(BaGetterOptions.Storage));
            app.Services.AddTransient<GoogleCloudStorageService>();

            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<GoogleCloudStorageService>());

            app.Services.AddProvider<IStorageService>((provider, config) =>
            {
                if (!config.HasStorageType("GoogleCloud")) return null;

                return provider.GetRequiredService<GoogleCloudStorageService>();
            });

            return app;
        }

        public static BaGetterApplication AddGoogleCloudStorage(
            this BaGetterApplication app,
            Action<GoogleCloudStorageOptions> configure)
        {
            app.AddGoogleCloudStorage();
            app.Services.Configure(configure);
            return app;
        }
    }
}
