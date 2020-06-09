using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace BaGet.Hosting
{
    public static class IHostExtensions
    {
        public static async Task RunMigrationsAsync(this IWebHost host, CancellationToken cancellationToken)
        {
            // Run migrations if necessary.
            var options = host.Services.GetRequiredService<IOptions<BaGetOptions>>();

            if (options.Value.RunMigrationsAtStartup && options.Value.Database.Type != DatabaseType.AzureTable)
            {
                using (var scope = host.Services.CreateScope())
                {
                    var ctx = scope.ServiceProvider.GetRequiredService<IContext>();

                    await ctx.RunMigrationsAsync(cancellationToken);
                }
            }
        }
    }
}
