using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Frontend.Mvc.Attributes;
using BaGet.Frontend.Mvc.UI;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of the account screens' JSON twins: accounts enabled (else 404), mail configured when <see cref="RequiresEmail"/>, signed in when
/// <see cref="RequiresSignIn"/> (else 401), through the same <see cref="ScreenGate"/> as <c>AccountControllerBase</c>. Account self-service
/// (password, two-factor, external logins, API keys, sign-in and out, deletion) and package ownership screens are <b>interactive only</b> (<see cref="CookieOnly"/>):
/// over HTTP they are open to a person's interactive session (a session token, or the cookie with an antiforgery header) and answer 403 "use the web screen" to
/// everybody else, API keys included; the web screen reaches them in process.
/// </summary>
public abstract class AccountApiControllerBase : ApiControllerBase, IAsyncActionFilter
{
    /// <summary>Whether the screen needs a signed-in user.</summary>
    protected virtual bool RequiresSignIn => false;

    /// <summary>Whether the screen needs mail to be configured.</summary>
    protected virtual bool RequiresEmail => false;

    /// <summary>Whether the twin is for interactive sessions only over HTTP (account self-service, package ownership): 403 for an API key and an anonymous caller. The administration bases lift it.</summary>
    protected virtual bool CookieOnly => true;

    /// <summary>What the screen requires of the caller; a derived base widens it (role, writes).</summary>
    protected virtual GateRequest Requirements(ActionExecutingContext context)
    {
        // The per-action forms of the two requirements, like AccountControllerBase reads them.
        var metadata = context.ActionDescriptor.EndpointMetadata ?? (IList<object>)Array.Empty<object>();

        return new GateRequest(
            RequiresSignIn: RequiresSignIn || metadata.OfType<RequiresSignInAttribute>().Any(),
            RequiresEmail: RequiresEmail || metadata.OfType<RequiresEmailAttribute>().Any(),
            KeyCaller: KeyCaller,

            // An endpoint that gives a session out (or prepares for one) is open to a caller without credentials; an API key is still refused.
            CookieOnly: CookieOnly && !(KeyCaller == null && metadata.OfType<AnonymousSessionEndpointAttribute>().Any()))
        {
            // This filter runs only when the twin is called over HTTP (the in-process call of a view controller skips it).
            OverHttp = true,
            InteractiveSession = InteractiveCaller != null,
        };
    }

    /// <summary>Asks the gate; a refusal is the answer.</summary>
    [NonAction]
    public virtual async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var gate = await Gate.CheckAsync(User, Requirements(context), HttpContext.RequestAborted);
        if (!gate.IsAllowed)
        {
            context.Result = new UiApiResult(Refusal(gate));
            return;
        }

        await next();
    }
}
