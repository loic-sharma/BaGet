using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BaGet.Web.V3.Extensions;

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

                    // The upgrade migration assigns every existing row to repository key 1; this creates the
                    // row that key refers to (a no-op when it exists, or when the database is not relational).
                    var repositories = scope.ServiceProvider.GetService<IRepositoryService>();
                    if (repositories != null)
                    {
                        await repositories.EnsureDefaultAsync(cancellationToken);

                        // Mirror:Enabled becomes the "mirror" remote repository the default repository fetches through.
                        var remotes = scope.ServiceProvider.GetService<IRemoteRepositoryAdministration>();
                        if (remotes != null)
                        {
                            await remotes.EnsureMirrorRepositoryAsync(cancellationToken);
                        }

                        // Repositories made private by AnonymousRead=false from before access control: reported, not changed.
                        var evaluator = scope.ServiceProvider.GetService<IAccessEvaluator>();
                        if (evaluator != null)
                        {
                            await AccessUpgradeCheck.WarnAsync(
                                repositories,
                                evaluator,
                                scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("BaGet.Access"),
                                cancellationToken);
                        }
                    }
                }
            }
        }
    }

    public static bool ValidateStartupOptions(this IHost host)
    {
        return host
            .Services
            .GetRequiredService<ValidateStartupOptions>()
            .Validate();
    }
}