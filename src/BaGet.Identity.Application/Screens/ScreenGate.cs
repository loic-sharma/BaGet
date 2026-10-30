using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// The single place that decides whether a caller may use a screen of the account and administration area: accounts enabled, mail
/// configured, signed in, administrator (or project administrator), the access model present, the server writable. The view
/// controller bases and the JSON API bases both ask it, in the same order, so the two ways to reach a screen cannot disagree. It
/// returns a <see cref="GateOutcome"/>; each side turns it into its own answer (a 404 page or a JSON 404, a sign-in redirect or a
/// 401). Resolved per request: the services it needs exist only while accounts are enabled.
/// </summary>
public sealed class ScreenGate
{
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly IOptions<EmailOptions> _email;
    private readonly IServiceProvider _services;

    /// <summary>Creates the gate.</summary>
    public ScreenGate(IOptions<AccountsOptions> accounts, IOptions<EmailOptions> email, IServiceProvider services)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Whether accounts are enabled.</summary>
    public bool AccountsEnabled => _accounts.Value.Enabled;

    /// <summary>Whether outgoing mail is configured.</summary>
    public bool EmailConfigured => _email.Value.IsConfigured;

    /// <summary>
    /// The synchronous part of the gate: accounts enabled, mail configured when required, the interactive-only rule, signed in when required.
    /// </summary>
    public GateOutcome CheckAccount(ClaimsPrincipal? user, GateRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (!AccountsEnabled) return GateOutcome.AccountsOff;
        if (request.RequiresEmail && !EmailConfigured) return GateOutcome.NotFound;
        if (request.CookieOnly && (request.KeyCaller != null || (request.OverHttp && !request.InteractiveSession))) return GateOutcome.Forbidden;
        if (request.RequiresSignIn && user?.Identity?.IsAuthenticated != true) return GateOutcome.SignIn;

        return GateOutcome.Allowed;
    }

    /// <summary>Whether the caller is a server administrator: the session's user, or the key of an administrator (see <see cref="IsAdministratorKey"/>).</summary>
    public async Task<bool> IsAdministratorAsync(ClaimsPrincipal? user, BaGetPrincipal? keyCaller, CancellationToken cancellationToken)
    {
        if (keyCaller != null) return IsAdministratorKey(keyCaller);
        if (user == null) return false;

        return await _services.GetRequiredService<AdministratorService>().IsAdministratorAsync(user, cancellationToken);
    }

    /// <summary>
    /// Whether an API key may use the administration screens: the key of an administrator that carries <see cref="ApiKeyScopes.Admin"/> and is not limited
    /// to repositories. The global <c>ApiKey</c> (the superuser) is refused here, unlike on the administration API (<c>/api/v1</c>): it has no owner, and the
    /// screens act as the signed-in user (an open server authenticates everybody as the superuser, which must never open the administration anyway).
    /// </summary>
    public bool IsAdministratorKey(BaGetPrincipal key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        // The global key has no owner: the use cases that need the current user could not act for it. The screens want an administrator's key.
        if (key.IsSuperuser) return false;

        return key.IsAdministrator && key.HasScope(ApiKeyScopes.Admin) && key.RepositoryKeys == null;
    }

    /// <summary>The whole gate for a screen.</summary>
    public async Task<GateResult> CheckAsync(ClaimsPrincipal? user, GateRequest request, CancellationToken cancellationToken)
    {
        var account = CheckAccount(user, request);
        if (account != GateOutcome.Allowed) return new GateResult(account, false) { Message = MessageFor(account, request) };

        if (request.Role == GateRole.Anyone) return new GateResult(GateOutcome.Allowed, false);

        var isAdministrator = await IsAdministratorAsync(user, request.KeyCaller, cancellationToken);

        if (request.Role == GateRole.Administrator)
        {
            return Decide(isAdministrator, request);
        }

        // The access screens need a relational database: an administrator is told so, anyone else sees nothing.
        if (_services.GetRequiredService<IContext>() is NullContext)
        {
            return new GateResult(isAdministrator ? GateOutcome.AccessUnavailable : GateOutcome.NotFound, isAdministrator);
        }

        if (!isAdministrator && (request.AlsoAllow == null || !CanDelegate(request.KeyCaller) || !await request.AlsoAllow(cancellationToken)))
        {
            return Decide(false, request);
        }

        if (request.IsWrite) _services.GetService<MaintenanceGate>()?.ThrowIfClosed();

        return new GateResult(GateOutcome.Allowed, isAdministrator);
    }

    // A key reaches the access screens only with the administration scope and no repository limit, as for the administration API
    // (a project administrator's other keys, e.g. package:push or limited to one repository, never administer); a session needs nothing more.
    private static bool CanDelegate(BaGetPrincipal? key) => key == null || (!key.IsSuperuser && key.HasScope(ApiKeyScopes.Admin) && key.RepositoryKeys == null);

    // A key that lacks the administration scope is told so (403); a session that is not an administrator learns nothing (404).
    private static GateOutcome Refusal(GateRequest request)
        => request.KeyCaller != null || request.InteractiveSession ? GateOutcome.Forbidden : GateOutcome.NotFound;

    /// <summary>The words an API client gets for a refusal of the gate (null where the outcome says it all).</summary>
    public const string ScreenOnlyMessage = "This screen is only available to an interactive session (it changes or reveals credentials or ownership, which an API key never does): use the web screen, or sign in at /api/ui/session and send the session token.";

    /// <summary>The words for a caller of <c>/api/ui</c> who is not authenticated.</summary>
    public const string KeyRequiredMessage = "Authenticate with an API key ('Authorization: Bearer <key>' or the 'X-NuGet-ApiKey' header) or a session token (POST /api/ui/session). The browser cookie is accepted only with a valid antiforgery header.";

    /// <summary>The words for the global <c>ApiKey</c> on an administration screen.</summary>
    public const string GlobalKeyMessage = "The global ApiKey cannot use the administration screens: use an administrator's API key with the admin:api scope and no repository limit.";

    /// <summary>The words for a key that is not an administrator's, lacks the admin:api scope or is limited to repositories.</summary>
    public const string AdminKeyMessage = "The administration screens need an administrator's API key with the admin:api scope and no repository limit.";

    /// <summary>The words for an interactive session that is not an administrator's, on an administration screen.</summary>
    public const string AdminSessionMessage = "The administration screens need an administrator's account.";

    private static string? MessageFor(GateOutcome outcome, GateRequest request)
        => outcome switch
        {
            GateOutcome.Forbidden when request.CookieOnly => ScreenOnlyMessage,
            GateOutcome.SignIn when request.OverHttp => KeyRequiredMessage,
            _ => null,
        };

    private static GateResult Decide(bool isAdministrator, GateRequest request)
    {
        if (isAdministrator) return new GateResult(GateOutcome.Allowed, true);

        var outcome = Refusal(request);
        var message = outcome != GateOutcome.Forbidden
            ? null
            : request.KeyCaller == null ? AdminSessionMessage : request.KeyCaller.IsSuperuser ? GlobalKeyMessage : AdminKeyMessage;

        return new GateResult(outcome, false) { Message = message };
    }
}
