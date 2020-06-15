using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BaGet
{
    // TODO: Move this to BaGet.Hosting.
    public static class IHostBuilderExtensions
    {
        public static IHostBuilder UseBaGet(this IHostBuilder host)
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

                config.AddKeyPerFile("/run/secrets", optional: true);
            });

            return host;
        }
    }
}
