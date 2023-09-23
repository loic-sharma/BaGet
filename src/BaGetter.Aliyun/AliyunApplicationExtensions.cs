using System;
using Aliyun.OSS;
using BaGetter.Aliyun;
using BaGetter.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGetter
{
    public static class AliyunApplicationExtensions
    {
        public static BaGetterApplication AddAliyunOssStorage(this BaGetterApplication app)
        {
            app.Services.AddBaGetterOptions<AliyunStorageOptions>(nameof(BaGetterOptions.Storage));

            app.Services.AddTransient<AliyunStorageService>();
            app.Services.TryAddTransient<IStorageService>(provider => provider.GetRequiredService<AliyunStorageService>());

            app.Services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AliyunStorageOptions>>().Value;

                return new OssClient(options.Endpoint, options.AccessKey, options.AccessKeySecret);
            });

            app.Services.AddProvider<IStorageService>((provider, config) =>
            {
                if (!config.HasStorageType("AliyunOss")) return null;

                return provider.GetRequiredService<AliyunStorageService>();
            });

            return app;
        }

        public static BaGetterApplication AddAliyunOssStorage(
            this BaGetterApplication app,
            Action<AliyunStorageOptions> configure)
        {
            app.AddAliyunOssStorage();
            app.Services.Configure(configure);
            return app;
        }
    }
}
