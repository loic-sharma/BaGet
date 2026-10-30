using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of every controller of the JSON UI API (<c>/api/ui/...</c>): the twin of a view controller that does the work of a screen and
/// returns its model, so a single-page application (or any client) can be written against it alone, and the view controller only renders
/// what <c>UiApiInvoker</c> got from it.
/// <list type="bullet">
/// <item><description><b>Authentication</b> (<see cref="UiApiSecurityFilter"/>): API keys (<c>Authorization: Bearer</c> or <c>X-NuGet-ApiKey</c>), like <c>/api/v1</c>, or a person's
/// interactive session (a session token from <c>/api/ui/session</c>, or the cookie with an antiforgery header); the bare cookie and HTTP Basic are never accepted
/// over HTTP (see <see cref="UiApiCaller"/>).</description></item>
/// <item><description><b>Answers</b> are <see cref="ScreenResult{T}"/> through <see cref="Respond{T}"/>: <c>status</c>, <c>model</c>, <c>errors</c>, <c>next</c>.</description></item>
/// <item><description><b>No <c>[ApiController]</c></b>: its automatic 400 on an invalid model would differ between an HTTP call and the
/// in-process call of a view controller. An action validates explicitly and answers <see cref="ScreenResult{T}.Invalid(T, string, int?)"/>.</description></item>
/// <item><description><b>Filters do not run in process</b>: the view controller's base runs the same <see cref="ScreenGate"/> before it calls the
/// twin, and its own antiforgery validation and cookie session.</description></item>
/// </list>
/// A twin is marked <see cref="UiApiAttribute"/> (never a view controller), routed under <c>api/ui/</c> (a repository
/// screen also at <c>api/ui/r/{repo}/...</c> through <see cref="RepositoryPageAttribute"/>), and takes the request body as JSON
/// (<c>[FromBody]</c>) or multipart (<c>[FromForm]</c>, file uploads).
/// </summary>
[TypeFilter(typeof(UiApiSecurityFilter))]
[Produces("application/json")]
// Not [ApiController], so ApiExplorer would skip the twins: they all belong in the OpenAPI document "ui".
[ApiExplorerSettings(IgnoreApi = false)]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The API key behind the request, or null for an anonymous caller or an interactive session.</summary>
    protected BaGetPrincipal? KeyCaller => UiApiCaller.Key(HttpContext);

    /// <summary>The interactive session behind the request (a session token, or the cookie with a valid antiforgery header), or null.</summary>
    protected BaGetPrincipal? InteractiveCaller => UiApiCaller.Interactive(HttpContext);

    /// <summary>The screen gate (the same one the view controllers ask).</summary>
    protected ScreenGate Gate
        => HttpContext.RequestServices.GetService<ScreenGate>()
           ?? ActivatorUtilities.CreateInstance<ScreenGate>(HttpContext.RequestServices);

    /// <summary>
    /// A use case, resolved when the action runs (not injected into the constructor): the account use cases are registered only while accounts are
    /// enabled, and the gate must be able to answer 404 first.
    /// </summary>
    [NonAction]
    protected T UseCase<T>() where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    /// <summary>
    /// What the request binding already refused (a value that is not a number, a malformed body), by field, for a use case that must not act while
    /// there is any. A use case validates its form itself; this only carries what it cannot see.
    /// </summary>
    [NonAction]
    protected IReadOnlyDictionary<string, string[]> BindingErrors()
        => ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "The value is not valid." : e.ErrorMessage).ToArray());

    /// <summary>The answer of an action: <paramref name="result"/> with the HTTP status of its decision.</summary>
    [NonAction]
    protected ActionResult<ScreenResult<T>> Respond<T>(ScreenResult<T> result)
        => new(new UiApiResult(result ?? throw new ArgumentNullException(nameof(result))));

    /// <summary>The answer for a gate's refusal: the screen has no model, so the result is of the action's own model type with only the decision set.</summary>
    [NonAction]
    protected static IScreenResult Refusal(GateResult gate)
        => gate.Message != null
            ? gate.Outcome switch
            {
                GateOutcome.SignIn => ScreenResult<object>.SignIn(gate.Message),
                GateOutcome.Forbidden => ScreenResult<object>.Forbidden(gate.Message),
                _ => Refusal(gate.Outcome),
            }
            : Refusal(gate.Outcome);

    /// <summary>The answer for a gate's refusal, without a message.</summary>
    [NonAction]
    protected static IScreenResult Refusal(GateOutcome outcome)
        => outcome switch
        {
            GateOutcome.SignIn => ScreenResult<object>.SignIn(),
            GateOutcome.Forbidden => ScreenResult<object>.Forbidden(),
            GateOutcome.AccessUnavailable => ScreenResult<object>.GoTo(new ScreenNext(ScreenCatalog.AdminAccessUnavailable)),
            _ => ScreenResult<object>.NotFound(),
        };
}
