using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet;

/// <summary>The administrator accounts a server needs at startup.</summary>
internal static class HostAdministratorExtensions
{
    /// <summary>
    /// Promote the users named by <c>Accounts:InitialAdministrators</c>; on a server with no users,
    /// create the default administrator unless <c>Accounts:CreateDefaultAdministrator</c> is off;
    /// and warn while it still has the default password. Does
    /// nothing while accounts are disabled, since then there is no <see cref="AdministratorService"/>.
    /// </summary>
    public static async Task BootstrapAdministratorsAsync(this WebApplication app, CancellationToken cancellationToken)
    {
        using var scope = app.Services.CreateScope();
        var administrators = scope.ServiceProvider.GetService<AdministratorService>();
        if (administrators == null) return;

        var options = scope.ServiceProvider.GetRequiredService<IOptions<AccountsOptions>>().Value;
        await administrators.PromoteInitialAdministratorsAsync(options.InitialAdministrators, cancellationToken);

        // Just created, it already said so; otherwise repeat the warning at every start until it is changed.
        var created = options.CreateDefaultAdministrator
            && await administrators.CreateDefaultAdministratorAsync(cancellationToken);
        if (!created && await administrators.HasDefaultPasswordAsync())
        {
            app.Logger.LogWarning(
                "The administrator {User} still has the default password. Sign in and change it.",
                AdministratorService.DefaultAdministratorUserName);
        }
    }
}
