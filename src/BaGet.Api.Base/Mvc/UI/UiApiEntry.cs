using System;
using System.Threading.Tasks;
using BaGet.Frontend.Controllers.Base;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Mvc.UI;

/// <summary>
/// The in-process call of a twin's <em>entry check</em>: a screen whose first step (ownership, a Delete permission, a valid name) must run
/// before the global repository filter, as it did on the page, asks it from <c>OnActionExecutionAsync</c> of its view controller. The twin
/// exposes the step as a <c>[NonAction]</c> method returning the refusal, or null to go on; the use case remembers the answer for the
/// request, so the operation that follows does not repeat the work. Same rules as <c>UiApiInvoker</c>: the twin shares the
/// view action's <see cref="ControllerContext"/>, its own filters do not run.
/// </summary>
public static class UiApiEntry
{
    /// <summary>Calls <paramref name="enter"/> on a new <typeparamref name="TApi"/> that shares the caller's context; null means "go on".</summary>
    /// <typeparam name="TApi">The twin controller.</typeparam>
    /// <typeparam name="TModel">The screen's model.</typeparam>
    /// <param name="caller">The view controller.</param>
    /// <param name="enter">The twin's entry check.</param>
    /// <returns>The refusal as a response the view controller renders, or null when the check passed.</returns>
    public static async Task<UiApiResponse<TModel>?> InvokeAsync<TApi, TModel>(
        ControllerBase caller,
        Func<TApi, Task<ScreenResult<TModel>?>> enter)
        where TApi : ApiControllerBase
    {
        if (caller == null) throw new ArgumentNullException(nameof(caller));
        if (enter == null) throw new ArgumentNullException(nameof(enter));

        var api = ActivatorUtilities.CreateInstance<TApi>(caller.HttpContext.RequestServices);
        api.ControllerContext = caller.ControllerContext;

        var refusal = await enter(api).ConfigureAwait(false);

        return refusal == null ? null : new UiApiResponse<TModel>(refusal, null);
    }
}
