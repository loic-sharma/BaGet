using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.UseCases.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of the access-control screens' JSON twins (groups, permissions, projects, audit; retention and replication build on it), like
/// <c>AccessControllerBase</c>: administrators, or whoever <see cref="AuthorizeAsync"/> admits (a project's administrators); an
/// administrator is pointed at <c>/admin/access-unavailable</c> where there is no access model; every write is refused with 503 while
/// the server is read-only.
/// </summary>
public abstract class AccessApiControllerBase : AdminApiControllerBase
{
    /// <summary>Admits a caller who is not a server administrator. The default admits nobody else.</summary>
    protected virtual Task<bool> AuthorizeAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>
    /// Who is using the screen, for a use case: the signed-in account (an API key's owner) and whether the caller is a server administrator rather than
    /// only a project administrator (decided by the same <see cref="ScreenGate"/> that admitted the request).
    /// </summary>
    [NonAction]
    protected async Task<AccessCaller> CallerAsync(CancellationToken cancellationToken)
        => new(User, await Gate.IsAdministratorAsync(User, KeyCaller, cancellationToken));

    /// <summary>The screen the running action serves (<see cref="UiApiAttribute.Screen"/>), so a base can widen one screen of a controller.</summary>
    [NonAction]
    protected ScreenRef? CurrentScreen()
        => ControllerContext.ActionDescriptor.EndpointMetadata.OfType<UiApiAttribute>().FirstOrDefault()?.Screen;

    /// <inheritdoc/>
    protected override GateRequest Requirements(ActionExecutingContext context)
        => base.Requirements(context) with
        {
            Role = GateRole.AccessAdministrator,
            IsWrite = !(HttpMethods.IsGet(Request.Method) || HttpMethods.IsHead(Request.Method)),
            AlsoAllow = AuthorizeAsync,
        };
}
