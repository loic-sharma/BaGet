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

                    // TODO: An "InvalidOperationException" is thrown and caught due to a bug
                    // in EF Core 3.0. This is fixed in 3.1.
                    // See: https://github.com/dotnet/efcore/issues/18307
                    await ctx.Database.MigrateAsync(cancellationToken);
                }
            }
        }
    }
}
