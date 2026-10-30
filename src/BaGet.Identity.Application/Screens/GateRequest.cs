using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;

namespace BaGet.Identity.Application.Screens;

/// <summary>What a screen requires of its caller, in the order the gate checks it.</summary>
/// <param name="RequiresSignIn">The caller must be signed in (else <see cref="GateOutcome.SignIn"/>).</param>
/// <param name="RequiresEmail">Mail must be configured (else <see cref="GateOutcome.NotFound"/>).</param>
/// <param name="Role">Who may use the screen.</param>
/// <param name="IsWrite">The request changes something: the read-only <see cref="MaintenanceGate"/> refuses it (<see cref="MaintenanceException"/>, 503) once the role is settled.</param>
/// <param name="KeyCaller">The API key behind the request, or null for a browser session (or an anonymous visitor).</param>
/// <param name="CookieOnly">The screen changes or reveals credentials: an API key never opens it (<see cref="GateOutcome.Forbidden"/>).</param>
/// <param name="AlsoAllow">For <see cref="GateRole.AccessAdministrator"/>: admits a caller who is not a server administrator (a project administrator).</param>
public sealed record GateRequest(
    bool RequiresSignIn = false,
    bool RequiresEmail = false,
    GateRole Role = GateRole.Anyone,
    bool IsWrite = false,
    BaGetPrincipal? KeyCaller = null,
    bool CookieOnly = false,
    Func<CancellationToken, Task<bool>>? AlsoAllow = null)
{
    /// <summary>
    /// The request reached the screen over HTTP as a JSON twin (<c>/api/ui</c>), where the only credential is an API key: a <see cref="CookieOnly"/> screen is then
    /// unreachable (<see cref="GateOutcome.Forbidden"/>, "use the web screen") for any caller, and a caller who is not signed in is told how to authenticate.
    /// The in-process call of a view controller leaves it false.
    /// </summary>
    public bool OverHttp { get; init; }

    /// <summary>
    /// The caller of a JSON twin is a person's interactive session (a session token, or the cookie with a valid antiforgery header), not an API key. A
    /// <see cref="CookieOnly"/> screen is open to it, and a caller who lacks the administrator role is told so (403) instead of the 404 a cookie visitor gets.
    /// </summary>
    public bool InteractiveSession { get; init; }
}
