using Aliyun.OSS;
using BaGet.Aliyun;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    public static class AliyunApplicationExtensions
    {
        public static BaGetApplication AddAliyunOssStorage(this BaGetApplication app)
        {
            app.Services.AddBaGetOptions<AliyunStorageOptions>(nameof(BaGetOptions.Storage));

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
    }
}
