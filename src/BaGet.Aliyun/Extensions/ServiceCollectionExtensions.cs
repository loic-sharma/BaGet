using Aliyun.OSS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Aliyun
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAliyunStorageService(this IServiceCollection services)
        {
            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AliyunStorageOptions>>().Value;

                return new OssClient(options.Endpoint, options.AccessKey, options.AccessKeySecret);
            });

            services.AddTransient<AliyunStorageService>();

            return services;
        }
    }
}
