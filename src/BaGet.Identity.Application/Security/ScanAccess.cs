using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Identity.Application.Security;

/// <summary>
/// Who may start a vulnerability scan of one version from the web UI: a signed-in user who may manage the package in the request's
/// repository (the Manage action of the access model plus ownership where the format has it, or an administrator). Scans cost the
/// server work and a call to the vulnerability source, so a reader cannot start them; the result itself is public to anyone who can
/// read the package.
/// </summary>
public static class ScanAccess
{
    /// <summary>Whether <paramref name="user"/> may scan <paramref name="name"/> in the request's repository.</summary>
    public static async Task<bool> CanScanAsync(IServiceProvider services, ClaimsPrincipal? user, string name, CancellationToken cancellationToken)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;

        var sessions = services.GetService<ISessionPrincipalFactory>();
        var principal = sessions == null ? null : await sessions.CreateAsync(user);
        if (principal == null) return false;

        // The same decision as the package's Manage page.
        return await services.GetRequiredService<IPackageAuthorizationService>().AuthorizeAsync(principal, PackageAction.Manage, name, cancellationToken);
    }
}
