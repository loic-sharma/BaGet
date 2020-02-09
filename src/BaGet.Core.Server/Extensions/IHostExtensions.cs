using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BaGet.Extensions
{
    public static class IHostExtensions
    {
        public static async Task RunMigrationsAsync(this IHost host, CancellationToken cancellationToken)
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
