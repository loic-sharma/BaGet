using System;
using BaGet.Core;
using BaGet.Bce;
using BaiduBce;
using BaiduBce.Auth;
using BaiduBce.Services.Bos;
using BaiduBce.Services.Bos.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public static class BceApplicationExtensions
    {
        public static BaGetApplication AddBceBosStorage(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<BceStorageOptions>(nameof(BaGetOptions.Storage));

            app.Services.AddTransient<BceStorageService>();
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<BceStorageService>());

            app.Services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<BceStorageOptions>>().Value;
                BceClientConfiguration config = new BceClientConfiguration();
                config.Credentials = new DefaultBceCredentials(options.AccessKey, options.AccessKeySecret);
                config.Endpoint = options.Endpoint;
                return new BosClient(config);
            });

            app.Services.AddProvider<IStorageService>((provider, config) =>
            {
                if (!config.HasStorageType("BceBos")) return null;

                return provider.GetRequiredService<BceStorageService>();
            });

            return app;
        }

        public static BaGetApplication AddBceBosStorage(
            this BaGetApplication app,
            Action<BceStorageOptions> configure)
        {
            app.AddBceBosStorage();
            app.Services.Configure(configure);
            return app;
        }
    }
}
