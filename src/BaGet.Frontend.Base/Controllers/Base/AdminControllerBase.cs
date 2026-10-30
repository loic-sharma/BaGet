using System.Threading.Tasks;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Services.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of the administrator screens. Signed in, accounts enabled (see
/// <see cref="AccountControllerBase"/>), and an administrator. Anyone else gets 404, so the screens' existence is not disclosed
/// (an anonymous visitor is challenged to sign in first, as the pages did).
/// </summary>
public abstract class AdminControllerBase : AccountControllerBase
{
    /// <inheritdoc />
    protected override bool RequiresSignIn => true;

    /// <summary>The administrator service, resolved per request: it exists only while accounts are enabled.</summary>
    protected AdministratorService Administrators
        => HttpContext.RequestServices.GetRequiredService<AdministratorService>();

    /// <inheritdoc />
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        OnActionExecuting(context);
        if (context.Result != null) return;

        if (!(await Gate.CheckAsync(User, new GateRequest(Role: GateRole.Administrator), HttpContext.RequestAborted)).IsAllowed)
        {
            context.Result = NotFound();
            return;
        }

        OnActionExecuted(await next());
    }

    /// <summary>
    /// Carries the outcome of an action across a redirect, as <c>[TempData] Message</c> / <c>[TempData] Error</c> did on the page
    /// models: a value the action just set is stored for the next request; otherwise the model shows (and consumes) what the previous
    /// request stored. Call it once per action, after the workflow ran and before returning.
    /// </summary>
    [NonAction]
    protected void Flash(IFlashMessage model)
    {
        if (model.Message != null) TempData["Message"] = model.Message;
        else model.Message = TempData["Message"] as string;
    }

    /// <summary><see cref="Flash(IFlashMessage)"/> for a screen that also flashes an error.</summary>
    [NonAction]
    protected void Flash(IFlashModel model)
    {
        Flash((IFlashMessage)model);

        if (model.Error != null) TempData["Error"] = model.Error;
        else model.Error = TempData["Error"] as string;
    }

    /// <summary>
    /// <c>Render</c> for a screen whose model carries the outcome of the last action across a redirect
    /// (<see cref="IFlashMessage"/>, <see cref="IFlashModel"/>): the message and error the previous request stored are moved into the model before the
    /// view renders, as <see cref="Flash(IFlashMessage)"/> did on the page models. A redirect (<c>Next</c> with a message) stores them for the next request.
    /// </summary>
    [NonAction]
    protected IActionResult RenderFlashed<TModel>(UiApiResponse<TModel> response, string view)
        where TModel : class, IFlashMessage
        => Render(
            response,
            view,
            model =>
            {
                if (model is IFlashModel flash) Flash(flash);
                else Flash((IFlashMessage)model);

                return model;
            });
}
