using System;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace BaGet
{
    // TODO: Move this to BaGet.Hosting.
    public static class IHostBuilderExtensions
    {
        public static IWebHostBuilder UseBaGet(this IWebHostBuilder host)
        {
            host.ConfigureServices((context, services) =>
            {
                services.AddBaGet(context.Configuration);
            });

            host.ConfigureAppConfiguration((context, config) =>
            {
                var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");

                if (!string.IsNullOrEmpty(root))
                {
                    config.SetBasePath(root);
                }
            });

            return host;
        }
    }
}
