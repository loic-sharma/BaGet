using System;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of every controller that serves a screen of BaGet's own web UI. What it gives a screen:
/// <list type="bullet">
/// <item><description><b>Antiforgery.</b> <see cref="AutoValidateAntiforgeryTokenAttribute"/>: every unsafe request (POST) without
/// a valid token is answered 400 before anything else runs, as for a page. Never opt a UI action out.</description></item>
/// <item><description><b>Handlers.</b> <see cref="PageHandlerAttribute"/> picks the action by <c>?handler=</c>, so form URLs
/// do not change; <see cref="RequestedHandler"/> is the value.</description></item>
/// <item><description><b>Filters.</b> The controller's own <c>OnActionExecuting</c>/<c>OnActionExecutionAsync</c> override runs first, before
/// the global <c>RepositoryActionFilter</c>.</description></item>
/// </list>
/// A screen is identified by its controller and its default GET action (<see cref="BaGet.Identity.Application.Screens.ScreenRef"/>); a UI controller
/// of a repository's page also carries <see cref="RepositoryPageAttribute"/>.
/// </summary>
/// <remarks>
/// <para><b>How to migrate a page</b>: the page's
/// <c>@page "/x"</c> becomes <c>[Route("x")]</c>; <c>OnGet</c> becomes <c>[HttpGet, HttpHead, PageHandler]</c>; <c>OnPostX</c>
/// becomes <c>[HttpPost, PageHandler("X")]</c> taking <c>[FromForm]</c>/<c>[FromQuery]</c> parameters instead of
/// <c>[BindProperty]</c>; <c>return Page()</c> becomes <c>return View("Name", model)</c>; <c>[TempData]</c> properties stay
/// <c>[TempData]</c> properties of the controller. The view moves to <c>Views/&lt;Controller&gt;/&lt;Name&gt;.cshtml</c> with
/// the view model as <c>@model</c>.</para>
/// </remarks>
[AutoValidateAntiforgeryToken]
public abstract class UiControllerBase : Controller, IScreenController
{
    /// <summary>
    /// The handler the request names (<c>?handler=</c>, or the <c>handler</c> route value), or null for the default handler.
    /// </summary>
    protected string? RequestedHandler => PageHandlerAttribute.RequestedHandler(HttpContext, RouteData);

    /// <summary>
    /// Calls the JSON twin of this screen in process (<see cref="UiApiInvoker"/>) and returns what it answered. The twin does the work; this
    /// controller only <c>Render</c>s the answer. The twin's filters do not run, so the controller base has run the screen gate
    /// and the antiforgery validation of the view action already.
    /// </summary>
    /// <typeparam name="TApi">The twin controller.</typeparam>
    /// <typeparam name="TModel">The screen's model.</typeparam>
    protected Task<UiApiResponse<TModel>> Api<TApi, TModel>(Func<TApi, Task<ActionResult<ScreenResult<TModel>>>> call)
        where TApi : ApiControllerBase
        => UiApiInvoker.InvokeAsync(this, call);

    /// <summary><c>Api</c> for a twin action that does not await.</summary>
    protected Task<UiApiResponse<TModel>> Api<TApi, TModel>(Func<TApi, ActionResult<ScreenResult<TModel>>> call)
        where TApi : ApiControllerBase
    {
        if (call == null) throw new ArgumentNullException(nameof(call));

        return UiApiInvoker.InvokeAsync<TApi, TModel>(this, api => Task.FromResult(call(api)));
    }

    /// <summary>
    /// Turns the answer of a twin into this controller's result: a plain action result the twin returned is returned unchanged; Ok and Invalid
    /// render <paramref name="view"/> (errors copied into <c>ModelState</c>, the result's status code applied); NotFound is 404, SignIn a challenge
    /// (the sign-in redirect), Forbidden a bare 403 (what a page answered), Next sets the flash message and redirects to the next screen by its
    /// controller and action (the repository stays), or to its local address.
    /// </summary>
    /// <param name="response">What <c>Api</c> returned.</param>
    /// <param name="view">The view to render.</param>
    /// <param name="viewModel">Maps the twin's model to the view's model when they differ (default: the model itself).</param>
    /// <param name="modelShowsFormErrors">
    /// The view prints the form-level message from its model (<c>Model.Error</c>), so the form-level errors (the empty key) are not copied into <c>ModelState</c> as
    /// well: the page would show the message twice. Field errors are always copied.
    /// </param>
    protected IActionResult Render<TModel>(UiApiResponse<TModel> response, string view, Func<TModel, object?>? viewModel = null, bool modelShowsFormErrors = false)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        return response.Raw ?? Render(response.Screen, view, viewModel, modelShowsFormErrors);
    }

    /// <summary>
    /// The same mapping for a result a controller got from a use case directly: only the browser-only steps (the external provider's challenge and
    /// callback) have no twin to call.
    /// </summary>
    protected IActionResult Render<TModel>(ScreenResult<TModel> screen, string view, Func<TModel, object?>? viewModel = null, bool modelShowsFormErrors = false)
    {
        if (screen == null) throw new ArgumentNullException(nameof(screen));

        switch (screen.Status)
        {
            case ScreenStatus.Ok:
            case ScreenStatus.Invalid:
                foreach (var pair in screen.Errors)
                {
                    if (modelShowsFormErrors && pair.Key.Length == 0) continue;

                    foreach (var message in pair.Value)
                    {
                        // A binding or validation error MVC already recorded for the field says the same: once is enough.
                        if (ModelState.TryGetValue(pair.Key, out var entry) && entry.Errors.Any(e => e.ErrorMessage == message)) continue;

                        ModelState.AddModelError(pair.Key, message);
                    }
                }

                var model = screen.Model == null ? null : viewModel != null ? viewModel(screen.Model) : screen.Model;
                var result = View(view, model);
                if (screen.StatusCode is { } code) result.StatusCode = code;

                return result;

            case ScreenStatus.NotFound:
                return NotFound();

            case ScreenStatus.SignIn:
                return Challenge();

            case ScreenStatus.Forbidden:
                // What a page answered: a bare 403 (Forbid() would hand the request to the cookie handler's access-denied redirect).
                return new StatusCodeResult(StatusCodes.Status403Forbidden);

            case ScreenStatus.Next:
                var next = screen.Next!;
                if (next.Message != null) TempData["Message"] = next.Message;
                if (next.Error != null) TempData["Error"] = next.Error;

                return next.LocalUrl != null
                    ? new LocalRedirectResult(next.LocalUrl) { UrlHelper = Url }
                    : new RedirectToActionResult(next.Screen!.Action, next.Screen.Controller, next.RouteValues, fragment: next.Fragment) { UrlHelper = Url };

            default:
                throw new InvalidOperationException($"Unknown screen status {screen.Status}.");
        }
    }
}
