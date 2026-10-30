using System;
using System.Threading.Tasks;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace BaGet.Frontend.Mvc.UI;

/// <summary>
/// The HTTP answer of a <see cref="ScreenResult{T}"/> on <c>/api/ui</c>: always <c>application/json</c> written with
/// <see cref="UiApiJson.Options"/> (camelCase, enums by name), with the status the decision maps to (or the screen's own <see cref="IScreenResult.StatusCode"/> when it sets one, e.g. 409 for a refused scan): Ok and Next 200, Invalid 400 (the body
/// still carries the model and the errors), NotFound 404, Forbidden 403, SignIn 401 with a <c>Bearer</c> challenge (not <c>Basic</c>, which
/// would make a browser prompt for a password on a <c>fetch</c>). A shown screen that carries its own status (<see cref="IScreenResult.StatusCode"/>) is answered with it. A maintenance refusal is not a result: it propagates and the middleware answers 503.
/// <para>
/// The in-process call of a view controller never executes it: <c>UiApiInvoker</c> reads <see cref="ObjectResult.Value"/>.
/// </para>
/// </summary>
public sealed class UiApiResult : ObjectResult
{
    private readonly IScreenResult _screen;

    /// <summary>Creates the answer for <paramref name="screen"/>.</summary>
    public UiApiResult(IScreenResult screen)
        : base(screen)
    {
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        StatusCode = StatusOf(screen);
        ContentTypes.Add("application/json");
        Formatters.Add(new SystemTextJsonOutputFormatter(UiApiJson.Options));
        DeclaredType = screen.GetType();
    }

    /// <summary>
    /// The HTTP status of a result: the status its screen renders with when the result sets one for a shown screen (<see cref="ScreenStatus.Ok"/>,
    /// <see cref="ScreenStatus.Invalid"/>: an upload refused with 403, a package page that explains a missing package with 404), else the one of its decision.
    /// </summary>
    public static int StatusOf(IScreenResult screen)
        => screen.StatusCode is { } code && screen.Status is ScreenStatus.Ok or ScreenStatus.Invalid ? code : StatusOf(screen.Status);

    /// <summary>The HTTP status of a decision.</summary>
    public static int StatusOf(ScreenStatus status) => status switch
    {
        ScreenStatus.Invalid => StatusCodes.Status400BadRequest,
        ScreenStatus.NotFound => StatusCodes.Status404NotFound,
        ScreenStatus.Forbidden => StatusCodes.Status403Forbidden,
        ScreenStatus.SignIn => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status200OK,
    };

    /// <inheritdoc/>
    public override Task ExecuteResultAsync(ActionContext context)
    {
        if (StatusCode == StatusCodes.Status401Unauthorized)
        {
            context.HttpContext.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer realm=\"BaGet\"";
        }

        return base.ExecuteResultAsync(context);
    }
}
