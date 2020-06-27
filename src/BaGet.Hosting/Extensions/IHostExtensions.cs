using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Hosting
{
    public static class IHostExtensions
    {
        public static IHostBuilder UseBaGet(this IHostBuilder host, Action<BaGetApplication> configure)
        {
            return host.ConfigureServices(services =>
            {
                services.AddBaGetWebApplication(configure);
            });
        }

        public static async Task RunMigrationsAsync(
            this IHost host,
            CancellationToken cancellationToken = default)
        {
            // Run migrations if necessary.
            var options = host.Services.GetRequiredService<IOptions<BaGetOptions>>();

            if (options.Value.RunMigrationsAtStartup)
            {
                using (var scope = host.Services.CreateScope())
                {
                    var ctx = scope.ServiceProvider.GetService<IContext>();
                    if (ctx != null)
                    {
                        await ctx.RunMigrationsAsync(cancellationToken);
                    }
                }
            }
        }

        public static bool ValidateOptions(this IHost host)
        {
            try
            {
                _ = host.Services.GetRequiredService<IOptions<BaGetOptions>>().Value;
                _ = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                _ = host.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
                _ = host.Services.GetRequiredService<IOptions<MirrorOptions>>().Value;

                return true;
            }
            catch (OptionsValidationException e)
            {
                var logger = host.Services.GetRequiredService<ILogger<BaGetOptions>>();

                foreach (var failure in e.Failures)
                {
                    logger.LogError("{ConfigFailure}", failure);
                }

                logger.LogError(e, "BaGet configuration is invalid.");

                return false;
            }
        }
    }
}
