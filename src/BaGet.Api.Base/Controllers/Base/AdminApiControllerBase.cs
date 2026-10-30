using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of the administrator screens' JSON twins: the caller is an administrator's API key with the <c>admin:api</c> scope and no repository limit
/// (see <see cref="ScreenGate.IsAdministratorKey"/>): 401 with no key, 403 for any other key, the global <c>ApiKey</c> included.
/// </summary>
public abstract class AdminApiControllerBase : AccountApiControllerBase
{
    /// <inheritdoc/>
    protected override bool RequiresSignIn => true;

    /// <inheritdoc/>
    protected override bool CookieOnly => false;

    /// <inheritdoc/>
    protected override GateRequest Requirements(ActionExecutingContext context)
        => base.Requirements(context) with { Role = GateRole.Administrator };
}
