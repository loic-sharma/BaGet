using System;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Mvc.UI;

/// <summary>
/// Calls a JSON twin from its view controller without an HTTP hop (decision D1). The twin is created like MVC creates a controller
/// (dependency injection) and given the view action's <see cref="ControllerContext"/>: the same <c>HttpContext</c> (so the same user, route
/// values, query and repository), the same <c>RouteData</c> and the same <see cref="Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary"/>.
/// Only the action method runs: the twin's filters do not (its <see cref="UiApiSecurityFilter"/>, its base's gate), so a view controller base
/// runs the same <see cref="ScreenGate"/> and the antiforgery validation of its own first. A <see cref="BaGet.Core.MaintenanceException"/> propagates
/// to the middleware (503). The twin must not use <c>[ApiController]</c> (see <see cref="ApiControllerBase"/>).
/// </summary>
public static class UiApiInvoker
{
    /// <summary>Calls <paramref name="call"/> on a new <typeparamref name="TApi"/> that shares the caller's context.</summary>
    /// <typeparam name="TApi">The twin controller.</typeparam>
    /// <typeparam name="TModel">The screen's model.</typeparam>
    /// <param name="caller">The view controller (its <see cref="ControllerBase.ControllerContext"/> is shared).</param>
    /// <param name="call">The action to call.</param>
    public static async Task<UiApiResponse<TModel>> InvokeAsync<TApi, TModel>(
        ControllerBase caller,
        Func<TApi, Task<ActionResult<ScreenResult<TModel>>>> call)
        where TApi : ApiControllerBase
    {
        if (caller == null) throw new ArgumentNullException(nameof(caller));
        if (call == null) throw new ArgumentNullException(nameof(call));

        var api = ActivatorUtilities.CreateInstance<TApi>(caller.HttpContext.RequestServices);
        api.ControllerContext = caller.ControllerContext;

        return Unwrap(await call(api));
    }

    // The twin answers with a ScreenResult (UiApiResult), or with a plain result: NotFound(), Forbid(), a status code.
    private static UiApiResponse<TModel> Unwrap<TModel>(ActionResult<ScreenResult<TModel>> action)
    {
        if (action.Value != null) return new UiApiResponse<TModel>(action.Value, null);

        switch (action.Result)
        {
            case ObjectResult { Value: ScreenResult<TModel> screen }:
                return new UiApiResponse<TModel>(screen, null);

            case NotFoundResult or NotFoundObjectResult:
                return new UiApiResponse<TModel>(ScreenResult<TModel>.NotFound(), action.Result);

            case UnauthorizedResult or UnauthorizedObjectResult or ChallengeResult:
                return new UiApiResponse<TModel>(ScreenResult<TModel>.SignIn(), action.Result);

            case ForbidResult:
                return new UiApiResponse<TModel>(ScreenResult<TModel>.Forbidden(), action.Result);

            case StatusCodeResult { StatusCode: 404 } or ObjectResult { StatusCode: 404 }:
                return new UiApiResponse<TModel>(ScreenResult<TModel>.NotFound(), action.Result);

            case StatusCodeResult { StatusCode: 401 } or ObjectResult { StatusCode: 401 }:
                return new UiApiResponse<TModel>(ScreenResult<TModel>.SignIn(), action.Result);

            case StatusCodeResult { StatusCode: 403 } or ObjectResult { StatusCode: 403 }:
                return new UiApiResponse<TModel>(ScreenResult<TModel>.Forbidden(), action.Result);

            case null:
                throw new InvalidOperationException("A UI API action returned neither a ScreenResult nor an action result.");

            default:
                // Any other answer (a redirect, a file) is passed on as it is; the decision is "next" for lack of a better word.
                return new UiApiResponse<TModel>(ScreenResult<TModel>.GoTo(new ScreenNext(null)), action.Result);
        }
    }
}
