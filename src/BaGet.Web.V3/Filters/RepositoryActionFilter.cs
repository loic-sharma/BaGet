using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Filters;
using BaGet.Web.V3.Routing;

namespace BaGet.Web.V3.Filters;

/// <summary>
/// The repository check of the controller actions that replaced Razor Pages: it applies to an action marked <see cref="RepositoryPageAttribute"/> (on the action or its controller). It runs the same
/// <see cref="RepositoryPageGate"/> with the rule the attribute states (format, upload, not on virtual / remote, front page). Registered with MVC itself, so a host cannot forget it, and inert for
/// every action without the attribute.
/// <para>
/// Order: global filters run after the controller's own <c>OnActionExecuting</c>/<c>OnActionExecutionAsync</c> override (the
/// controller is a filter of order <c>int.MinValue</c>), so a controller check that must precede the repository check does.
/// </para>
/// </summary>
public class RepositoryActionFilter : IAsyncActionFilter
{
    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attribute = context.ActionDescriptor.EndpointMetadata.OfType<RepositoryPageAttribute>().LastOrDefault();
        if (attribute != null)
        {
            var rule = new RepositoryPageRule(
                Format: attribute.AnyFormat ? null : attribute.Format,
                Upload: attribute.Upload,
                NotOnVirtual: attribute.NotOnVirtual,
                NotOnRemote: attribute.NotOnRemote,
                FrontPage: attribute.FrontPage);

            var result = await RepositoryPageGate.CheckAsync(context.HttpContext, context.RouteData, rule);

            // The JSON twin of a screen answers in JSON: 401 for a visitor who must sign in, 404 for what the caller may not see. An API
            // key limited to repositories sees a repository outside its limit as a signed-in user sees one it may not read: 404.
            var isApi = context.ActionDescriptor.EndpointMetadata.OfType<UiApiAttribute>().Any();
            if (isApi)
            {
                var key = UiApiCaller.Key(context.HttpContext);
                if (result == null && key != null
                    && !key.AllowsRepository(context.HttpContext.RequestServices.GetRequiredService<IRepositoryContext>().Current.Key))
                {
                    result = new NotFoundResult();
                }

                result = result switch
                {
                    null => null,
                    ChallengeResult => new UiApiResult(ScreenResult<object>.SignIn()),
                    NotFoundResult => new UiApiResult(ScreenResult<object>.NotFound()),
                    RedirectToActionResult redirect => new UiApiResult(ScreenResult<object>.GoTo(new ScreenNext(
                        new ScreenRef(redirect.ControllerName!, redirect.ActionName!),
                        redirect.RouteValues?.ToDictionary(p => p.Key, p => (object?)p.Value)))),
                    _ => result,
                };
            }

            if (result != null)
            {
                context.Result = result;
                return;
            }
        }

        await next();
    }
}
