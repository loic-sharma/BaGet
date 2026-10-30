using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Links;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// Signing in, and proving presence again, through the external (OpenID Connect) provider. These steps are <b>browser-only</b>: the provider
/// redirects the browser and answers into a cookie, so they have no JSON twin; the view controller sends the challenge and handles the callback.
/// The rules are the same ones the pages applied: the provider is the gate for a new account (registration need not be open), only a provider-verified
/// address is confirmed, an unconfirmed or pending account never gets a second account made for it, and a re-authentication must be this account's own
/// login with a fresh <c>auth_time</c>.
/// </summary>
public sealed class ExternalSignInUseCase
{
    /// <summary>The name of the OpenID Connect scheme <c>AddBaGetAccounts</c> registers.</summary>
    public const string OpenIdConnectScheme = "oidc";

    /// <summary>The id token claim that tells when the user last authenticated at the provider.</summary>
    public const string AuthTimeClaim = "auth_time";

    /// <summary>Allowed difference between the provider's clock and ours.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    private readonly UserManager<BaGetUser> _users;
    private readonly SignInManager<BaGetUser> _signIn;
    private readonly PackageOwnershipService _ownership;
    private readonly IEmailSender<BaGetUser> _sender;
    private readonly IHttpContextAccessor _http;
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly IOptions<EmailOptions> _email;
    private readonly IOptions<SiteOptions> _site;
    private readonly SystemTime _time;
    private readonly SignInUseCase _signInScreens;
    private readonly OpenIdConnectProviderSet _providers;
    private readonly ExternalProviderMapping? _mapping;
    private readonly ExternalLogins _logins;
    private readonly ILogger<ExternalSignInUseCase> _logger;

    /// <summary>Creates the use case.</summary>
    public ExternalSignInUseCase(
        UserManager<BaGetUser> users,
        SignInManager<BaGetUser> signIn,
        PackageOwnershipService ownership,
        IEmailSender<BaGetUser> sender,
        IHttpContextAccessor http,
        IOptions<AccountsOptions> accounts,
        IOptions<EmailOptions> email,
        IOptions<SiteOptions> site,
        SystemTime time,
        SignInUseCase signInScreens,
        ILogger<ExternalSignInUseCase> logger)
        : this(users, signIn, ownership, sender, http, accounts, email, site, time, signInScreens, logger, OpenIdConnectProviders.Resolve(accounts.Value), ExternalLogins.For(users, accounts), null)
    {
    }

    /// <summary>Creates the use case; a <c>mapping</c> applies a provider's role and group mapping (null applies none).</summary>
    public ExternalSignInUseCase(
        UserManager<BaGetUser> users,
        SignInManager<BaGetUser> signIn,
        PackageOwnershipService ownership,
        IEmailSender<BaGetUser> sender,
        IHttpContextAccessor http,
        IOptions<AccountsOptions> accounts,
        IOptions<EmailOptions> email,
        IOptions<SiteOptions> site,
        SystemTime time,
        SignInUseCase signInScreens,
        ILogger<ExternalSignInUseCase> logger,
        OpenIdConnectProviderSet providers,
        ExternalLogins logins,
        ExternalProviderMapping? mapping = null)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _signInScreens = signInScreens ?? throw new ArgumentNullException(nameof(signInScreens));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _mapping = mapping;
        _logins = logins ?? throw new ArgumentNullException(nameof(logins));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private bool EmailConfigured => _email.Value.IsConfigured;

    private HttpContext Http => _http.HttpContext ?? throw new InvalidOperationException("External sign-in needs a request.");

    // ----- sign in with the provider

    /// <summary>The "sign in with the provider" button of a server with one provider: the challenge to send, or null when there is not exactly one provider (404).</summary>
    /// <param name="callbackUrl">The address of the callback handler of the sign-in screen (built by the view with its URL helper).</param>
    public ExternalChallenge? StartSignIn(string callbackUrl) => StartSignIn(null, callbackUrl);

    /// <summary>
    /// The "sign in with the provider" button: the challenge to send, or null when the provider is unknown (404). A request that names no provider
    /// means the only configured one (a form of before several were supported); with several, a provider must be named.
    /// </summary>
    /// <param name="provider">The provider id, or null.</param>
    /// <param name="callbackUrl">The address of the callback handler of the sign-in screen (built by the view with its URL helper).</param>
    public ExternalChallenge? StartSignIn(string? provider, string callbackUrl)
    {
        var chosen = string.IsNullOrEmpty(provider) ? (_providers.All.Count == 1 ? _providers.All[0] : null) : _providers.Find(provider);
        if (chosen == null) return null;

        var properties = _signIn.ConfigureExternalAuthenticationProperties(chosen.Id, callbackUrl);

        return new ExternalChallenge(chosen.Id, properties);
    }

    /// <summary>
    /// The provider has signed the user in. The first time, an account is created for them: the provider is the gate, so this does not
    /// depend on <c>AllowRegistration</c>.
    /// </summary>
    public Task<ScreenResult<LoginViewModel>> CompleteSignInAsync(string? returnUrl, CancellationToken cancellationToken)
        => CompleteCoreAsync(returnUrl, cookie: true, cancellationToken);

    /// <summary>The account the last <see cref="CompleteForSessionAsync"/> signed in (or created), or null.</summary>
    internal BaGetUser? SignedInUser { get; private set; }

    /// <summary>
    /// The same decision as <see cref="CompleteSignInAsync"/> for a client that holds tokens: no cookie is set, so an unrelated cookie session of the browser
    /// is left alone. A success is <see cref="ScreenStatus.Next"/> with the account in <see cref="SignedInUser"/>; the second factor is the session use case's.
    /// </summary>
    internal Task<ScreenResult<LoginViewModel>> CompleteForSessionAsync(CancellationToken cancellationToken)
        => CompleteCoreAsync(null, cookie: false, cancellationToken);

    // What ExternalLoginSignInAsync decides, without writing a cookie.
    private async Task<SignInResult> CheckExternalAsync(ExternalLoginInfo info)
    {
        var existing = await _users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (existing == null) return SignInResult.Failed;
        if (await _users.IsLockedOutAsync(existing)) return SignInResult.LockedOut;
        if (!await _signIn.CanSignInAsync(existing)) return SignInResult.NotAllowed;

        SignedInUser = existing;

        return SignInResult.Success;
    }

    private async Task<ScreenResult<LoginViewModel>> CompleteCoreAsync(string? returnUrl, bool cookie, CancellationToken cancellationToken)
    {
        var model = _signInScreens.Describe(new LoginViewModel { ReturnUrl = returnUrl });
        SignedInUser = null;

        var info = await _signIn.GetExternalLoginInfoAsync();
        if (info == null) return Refuse(model, "The external sign-in failed.");

        // The provider that answered: its claim names and its role/group mapping. An answer of a handler that is no longer configured has
        // none of either (the defaults apply).
        var provider = _providers.Find(info.LoginProvider);
        var options = provider?.Options ?? new OpenIdConnectProviderOptions();

        var known = await _users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (known != null && provider != null && !await _users.IsLockedOutAsync(known) && await _signIn.CanSignInAsync(known))
        {
            // The provider vouches for this sign-in, so its administrator and group claims apply before the session is created.
            await ApplyMappingAsync(provider, known, info.Principal, cancellationToken);
        }

        var result = cookie
            ? await _signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false)
            : await CheckExternalAsync(info);
        if (result.Succeeded)
        {
            return ScreenResult<LoginViewModel>.GoTo(ScreenNext.Local(LocalUrls.Target(returnUrl)));
        }

        if (result.RequiresTwoFactor)
        {
            return ScreenResult<LoginViewModel>.GoTo(new ScreenNext(ScreenCatalog.AccountLoginWith2fa, new Dictionary<string, object?> { ["ReturnUrl"] = returnUrl }));
        }

        if (result.IsLockedOut) return Refuse(model, SignInUseCase.LockedMessage);

        if (result.IsNotAllowed)
        {
            // This provider login already belongs to an account, which cannot sign in yet
            // (its email is unconfirmed). Never make another account for it.
            var existing = await _users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existing is { PendingApproval: true }) return Refuse(model, SignInUseCase.PendingMessage);

            if (existing != null && await TrySendConfirmationAsync(existing))
            {
                model.Notice = "Confirm your email address before signing in. We sent you a new confirmation link.";

                return ScreenResult<LoginViewModel>.Ok(model);
            }

            return Refuse(model, SignInUseCase.UnconfirmedMessage);
        }

        var email = ExternalClaims.First(info.Principal, OpenIdConnectProviders.EmailClaim(options));
        if (EmailConfigured && string.IsNullOrWhiteSpace(email))
        {
            return Refuse(model, "The external account has no email address, which this server needs to confirm accounts.");
        }

        var name = await ChooseUserNameAsync(info.Principal, options, cancellationToken);
        if (name == null) return Refuse(model, "The external account has no usable name.");

        var user = new BaGetUser
        {
            UserName = name,
            Email = email,

            // Only an address the provider vouches for is confirmed; any other must be
            // proven by following a link, like one typed into the registration form.
            EmailConfirmed = !string.IsNullOrWhiteSpace(email) && ProviderVerifiedEmail(info.Principal, options),
            Created = _time.UtcNow,
            PendingApproval = _accounts.Value.RequireApproval,
        };

        var created = await _users.CreateAsync(user);
        if (created.Succeeded)
        {
            created = await _users.AddLoginAsync(user, info);
        }

        if (!created.Succeeded)
        {
            // Do not leave an account behind that has no login to reach it.
            if (user.Id != null && await _users.FindByIdAsync(user.Id) != null) await _users.DeleteAsync(user);

            return Refuse(model, string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        if (provider != null) await ApplyMappingAsync(provider, user, info.Principal, cancellationToken);

        if (user.PendingApproval)
        {
            // Approval does not replace the address check: send the link now, so approving is the last step.
            if (EmailConfigured && !user.EmailConfirmed) await TrySendConfirmationAsync(user);

            model.Notice = "Your account was created. An administrator must enable it before you can sign in.";

            return ScreenResult<LoginViewModel>.Ok(model);
        }

        if (EmailConfigured && !user.EmailConfirmed)
        {
            model.Notice = await TrySendConfirmationAsync(user)
                ? "Your account was created. Confirm your email address with the link we sent you, then sign in again."
                : "Your account was created, but we could not send the confirmation email. Use \"Forgot password\" to get a link later.";

            return ScreenResult<LoginViewModel>.Ok(model);
        }

        SignedInUser = user;
        if (cookie) await _signIn.SignInAsync(user, isPersistent: false);

        return ScreenResult<LoginViewModel>.GoTo(ScreenNext.Local(LocalUrls.Target(returnUrl)));
    }

    private Task ApplyMappingAsync(OpenIdConnectProvider provider, BaGetUser user, ClaimsPrincipal principal, CancellationToken cancellationToken)
        => _mapping == null ? Task.CompletedTask : _mapping.ApplyAsync(provider, user, principal, cancellationToken);

    private static ScreenResult<LoginViewModel> Refuse(LoginViewModel model, string message)
    {
        model.Error = message;

        return ScreenResult<LoginViewModel>.Invalid(model, message);
    }

    private static bool ProviderVerifiedEmail(ClaimsPrincipal principal, OpenIdConnectProviderOptions options)
        => bool.TryParse(ExternalClaims.First(principal, OpenIdConnectProviders.EmailVerifiedClaim(options)), out var verified) && verified;

    private async Task<bool> TrySendConfirmationAsync(BaGetUser user)
    {
        if (!EmailConfigured || string.IsNullOrEmpty(user.Email) || user.EmailConfirmed) return false;

        try
        {
            var token = await _users.GenerateEmailConfirmationTokenAsync(user);
            var link = AccountLinks.ConfirmEmail(_site, user.Id, token);
            await _sender.SendConfirmationLinkAsync(user, user.Email, link);

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not send a confirmation link.");

            return false;
        }
    }

    /// <summary>
    /// The provider's preferred user name, else the email's local part, made unique across users and organizations by a numeric suffix.
    /// </summary>
    private async Task<string?> ChooseUserNameAsync(ClaimsPrincipal principal, OpenIdConnectProviderOptions options, CancellationToken cancellationToken)
    {
        var candidate = ExternalClaims.First(principal, OpenIdConnectProviders.UserNameClaim(options))
                        ?? ExternalClaims.First(principal, OpenIdConnectProviders.EmailClaim(options))?.Split('@')[0]
                        ?? principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(candidate)) return null;

        var allowed = _users.Options.User.AllowedUserNameCharacters;
        candidate = new string(candidate.Where(c => allowed.Contains(c)).ToArray());
        if (candidate.Length == 0) return null;

        var name = candidate;
        for (var suffix = 2; await _ownership.AccountExistsAsync(name, cancellationToken); suffix++)
        {
            name = candidate + suffix;
        }

        return name;
    }

    // ----- proof of presence for an account without a password

    /// <summary>The "sign in again" button for the only linked provider (or the screen to pick one); see the overload with a provider.</summary>
    public Task<(ExternalChallenge? Challenge, ScreenResult<ReauthenticateViewModel>? Screen)> StartReauthenticationAsync(ClaimsPrincipal caller, string callbackUrl, string? returnUrl)
        => StartReauthenticationAsync(caller, callbackUrl, returnUrl, null);

    /// <summary>
    /// The "sign in again" button: a fresh sign-in with a provider the account has linked (it is asked to prompt for credentials again, whatever
    /// session it has). With several linked providers and none named, the answer is the screen that lets the user pick (no challenge). The time is kept
    /// in the sign-in cookie's properties and checked by <see cref="AccountConfirmation.IsConfirmedAsync"/> for
    /// <see cref="AccountConfirmation.ReauthenticationWindow"/>.
    /// </summary>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="callbackUrl">The address of the callback handler of the screen.</param>
    /// <param name="returnUrl">Where to continue after the provider answered.</param>
    /// <param name="provider">The provider to use, or null to take the only linked one (or offer the choice).</param>
    /// <returns>The challenge to send, or a screen (sign in, the refusal, or the choice of provider).</returns>
    public async Task<(ExternalChallenge? Challenge, ScreenResult<ReauthenticateViewModel>? Screen)> StartReauthenticationAsync(ClaimsPrincipal caller, string callbackUrl, string? returnUrl, string? provider)
    {
        var user = await _users.GetUserAsync(caller);
        if (user == null) return (null, ScreenResult<ReauthenticateViewModel>.SignIn());

        var linked = await _logins.LinkedAsync(user);
        if (linked.Count == 0)
        {
            return (null, Refuse(new ReauthenticateViewModel { ReturnUrl = returnUrl }, "There is no external provider linked to this account to sign in with."));
        }

        ExternalProviderInfo? chosen;
        if (string.IsNullOrEmpty(provider))
        {
            if (linked.Count > 1)
            {
                // Several providers are linked: the user picks the one to prove it with.
                return (null, ScreenResult<ReauthenticateViewModel>.Ok(new ReauthenticateViewModel { ReturnUrl = returnUrl, Providers = linked.ToList() }));
            }

            chosen = linked[0];
        }
        else
        {
            chosen = linked.FirstOrDefault(p => string.Equals(p.Id, provider, StringComparison.OrdinalIgnoreCase));
            if (chosen == null)
            {
                return (null, Refuse(new ReauthenticateViewModel { ReturnUrl = returnUrl, Providers = linked.Count > 1 ? linked.ToList() : null }, "That provider is not linked to this account."));
            }
        }

        var properties = _signIn.ConfigureExternalAuthenticationProperties(chosen.Id, callbackUrl, user.Id);

        // Ask the provider to authenticate again whatever session it holds.
        properties.SetParameter(OpenIdConnectChallengeProperties.PromptKey, "login");
        properties.SetParameter(OpenIdConnectChallengeProperties.MaxAgeKey, TimeSpan.Zero);

        return (new ExternalChallenge(chosen.Id, properties), null);
    }

    /// <summary>True when <paramref name="authTime"/> (seconds since the epoch) is present and within the window.</summary>
    internal static bool IsFresh(string? authTime, DateTime now)
    {
        if (!long.TryParse(authTime, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            || seconds < 0 || seconds > 253402300799L)
        {
            return false;
        }

        var age = now - DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

        return age >= -ClockSkew && age <= AccountConfirmation.ReauthenticationWindow;
    }

    /// <summary>The provider answered.</summary>
    public async Task<ScreenResult<ReauthenticateViewModel>> CompleteReauthenticationAsync(ClaimsPrincipal caller, string? returnUrl)
    {
        var model = new ReauthenticateViewModel { ReturnUrl = returnUrl };
        var httpContext = Http;

        var user = await _users.GetUserAsync(caller);
        if (user == null) return ScreenResult<ReauthenticateViewModel>.SignIn();

        // The anti-forgery value is the signed-in user's id, so an answer started by
        // someone else's session is not accepted.
        var info = await _signIn.GetExternalLoginInfoAsync(user.Id);
        await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        // It must be this account's own login, not any account at the provider.
        var owner = info == null ? null : await _users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (owner == null || owner.Id != user.Id)
        {
            return Refuse(model, "The sign-in did not match this account.");
        }

        // The provider may ignore prompt=login / max_age (the handler does not enforce max_age),
        // so the id_token's own auth_time (required by OIDC Core when max_age is sent) must show
        // a sign-in within the window.
        var now = _time.UtcNow;
        if (!IsFresh(info?.Principal?.FindFirst(AuthTimeClaim)?.Value, now))
        {
            return Refuse(model, "The provider did not confirm a fresh sign-in. Sign in again and enter your credentials when asked.");
        }

        var authentication = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var properties = authentication.Properties ?? new AuthenticationProperties();
        properties.Items[AccountConfirmation.ReauthenticatedItem] = now.Ticks.ToString();

        // What RefreshSignInAsync keeps, plus the new property; the stamp is unchanged.
        var kept = new[] { ClaimTypes.AuthenticationMethod, "amr" }
            .Select(type => authentication.Principal?.FindFirst(type))
            .Where(claim => claim != null)
            .ToList();
        #pragma warning disable CS8620
        await _signIn.SignInWithClaimsAsync(user, properties, kept);
        #pragma warning restore CS8620

        return ScreenResult<ReauthenticateViewModel>.GoTo(ScreenNext.Local(LocalUrls.AppRelative(LocalUrls.Target(returnUrl))));
    }

    private static ScreenResult<ReauthenticateViewModel> Refuse(ReauthenticateViewModel model, string message)
    {
        model.Error = message;

        return ScreenResult<ReauthenticateViewModel>.Invalid(model, message);
    }

    /// <summary>The "confirm it is you" screen.</summary>
    public static ScreenResult<ReauthenticateViewModel> ShowReauthenticate(string? returnUrl)
        => ScreenResult<ReauthenticateViewModel>.Ok(new ReauthenticateViewModel { ReturnUrl = returnUrl });
}
