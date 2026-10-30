using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.Links;
using BaGet.Identity.Application.Models.Account;
using BaGet.Identity.Application.RateLimiting;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>
/// Creating an account and the mail round trips around it: registration, the confirmation link, "forgot password" and the new-password form. The
/// screen answers 404 while <c>Accounts:AllowRegistration</c> is off (registration only).
/// </summary>
public sealed class RegistrationUseCase
{
    private const string InvalidLink = "This link is invalid, expired or already used.";

    private readonly UserManager<BaGetUser> _users;
    private readonly SignInManager<BaGetUser> _signIn;
    private readonly PackageOwnershipService _ownership;
    private readonly IEmailSender<BaGetUser> _sender;
    private readonly IOptions<AccountsOptions> _accounts;
    private readonly IOptions<EmailOptions> _email;
    private readonly IOptions<SiteOptions> _site;
    private readonly SystemTime _time;
    private readonly IServiceProvider _services;
    private readonly ILogger<RegistrationUseCase> _logger;

    /// <summary>Creates the use case.</summary>
    public RegistrationUseCase(
        UserManager<BaGetUser> users,
        SignInManager<BaGetUser> signIn,
        PackageOwnershipService ownership,
        IEmailSender<BaGetUser> sender,
        IOptions<AccountsOptions> accounts,
        IOptions<EmailOptions> email,
        IOptions<SiteOptions> site,
        SystemTime time,
        IServiceProvider services,
        ILogger<RegistrationUseCase> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private bool EmailConfigured => _email.Value.IsConfigured;

    // ----- registration

    /// <summary>The registration form.</summary>
    public ScreenResult<RegisterViewModel> ShowRegister(string? returnUrl)
        => _accounts.Value.AllowRegistration
            ? ScreenResult<RegisterViewModel>.Ok(new RegisterViewModel { ReturnUrl = returnUrl, EmailRequired = EmailConfigured })
            : ScreenResult<RegisterViewModel>.NotFound();

    /// <summary>The registration form was posted.</summary>
    public async Task<ScreenResult<RegisterViewModel>> RegisterAsync(RegisterForm? form, CancellationToken cancellationToken)
    {
        if (!_accounts.Value.AllowRegistration) return ScreenResult<RegisterViewModel>.NotFound();

        // Never the passwords: the screen's model repeats the harmless fields only.
        var model = new RegisterViewModel
        {
            UserName = form?.UserName,
            Email = form?.Email,
            ReturnUrl = form?.ReturnUrl,
            EmailRequired = EmailConfigured,
        };

        var errors = ScreenValidator.Validate(form);
        if (form != null && EmailConfigured && string.IsNullOrWhiteSpace(form.Email))
        {
            ScreenValidator.With(errors, nameof(RegisterForm.Email), "An email address is required to confirm the account.");
        }

        if (errors.Count > 0) return ScreenResult<RegisterViewModel>.Invalid(model, errors);

        // Over /api/ui (no cookie is set for such a caller) every attempt counts against the client address (a backstop). The per-email allowance is only taken once an
        // account exists (below), so a refused attempt (name taken, weak password) never blocks a retry and nobody can use up the allowance of an address they do not own.
        var http = _services.GetService<IHttpContextAccessor>()?.HttpContext;
        var overHttp = http != null && AccountConfirmation.IsOverHttp(http);
        var client = http?.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        if (overHttp && !_services.GetRequiredService<PasswordResetRateLimiter>().TryAcquire(client, "attempt:" + Guid.NewGuid().ToString("N")))
        {
            return ScreenResult<RegisterViewModel>.Invalid(model, "Too many attempts. Try again in a few minutes.");
        }

        // User and organization names share a namespace: either can own a package.
        if (await _ownership.AccountExistsAsync(form!.UserName, cancellationToken))
        {
            return ScreenResult<RegisterViewModel>.Invalid(model, ScreenValidator.With(new Dictionary<string, string[]>(), nameof(RegisterForm.UserName), "That name is taken."));
        }

        var user = new BaGetUser
        {
            UserName = form.UserName,
            Email = form.Email,
            Created = _time.UtcNow,
            PendingApproval = _accounts.Value.RequireApproval,
        };

        #pragma warning disable CS8604
        var result = await _users.CreateAsync(user, form.Password);
        #pragma warning restore CS8604
        if (!result.Succeeded)
        {
            return ScreenResult<RegisterViewModel>.Invalid(
                model,
                new Dictionary<string, string[]> { [string.Empty] = result.Errors.Select(e => e.Description).ToArray() });
        }

        if (user.PendingApproval)
        {
            _logger.LogWarning("{User} registered and awaits approval", user.UserName);
        }

        if (EmailConfigured)
        {
            // Over /api/ui the address is mailed once per window (a confirmation was already sent to it a moment ago otherwise: "resend" mails another later).
            if (overHttp
                && !string.IsNullOrWhiteSpace(user.Email)
                && !_services.GetRequiredService<PasswordResetRateLimiter>().TryAcquire(client, "register:" + user.Email.Trim().ToLowerInvariant()))
            {
                model.ConfirmationSent = true;
                model.AwaitingApproval = user.PendingApproval;

                return ScreenResult<RegisterViewModel>.Ok(model);
            }

            // Signing in needs a confirmed address, so the user is not signed in yet.
            var token = await _users.GenerateEmailConfirmationTokenAsync(user);
            var link = AccountLinks.ConfirmEmail(_site, user.Id, token);

            try
            {
                #pragma warning disable CS8604
                await _sender.SendConfirmationLinkAsync(user, user.Email, link);
                #pragma warning restore CS8604
                model.ConfirmationSent = true;
                model.AwaitingApproval = user.PendingApproval;

                return ScreenResult<RegisterViewModel>.Ok(model);
            }
            catch (Exception)
            {
                // The account exists; "Forgot password" mails a link that also confirms the address.
                return ScreenResult<RegisterViewModel>.Invalid(
                    model,
                    "Your account was created, but we could not send the confirmation email. Use \"Forgot password\" to get a link later."
                    + (user.PendingApproval ? " An administrator must also enable your account before you can sign in." : string.Empty));
            }
        }

        if (user.PendingApproval)
        {
            // Not signed in: an administrator enables the account first.
            model.AwaitingApproval = true;

            return ScreenResult<RegisterViewModel>.Ok(model);
        }

        if (overHttp)
        {
            // No cookie for an HTTP caller (nothing would stop a cross-site page from signing a visitor in): the new account's tokens, or nothing.
            model.Session = await _services.GetRequiredService<SessionUseCase>().IssueAsync(user, _time.UtcNow);

            return ScreenResult<RegisterViewModel>.Ok(model);
        }

        await _signIn.SignInAsync(user, isPersistent: false);

        return ScreenResult<RegisterViewModel>.GoTo(ScreenNext.Local(LocalUrls.Target(form.ReturnUrl)));
    }

    // ----- the confirmation link again

    /// <summary>
    /// Mails a new confirmation link to an account whose address is not confirmed yet. Like "forgot password" it answers the same, in the same time, whether or
    /// not an account matches or the mail was allowed (one per account per window, and per client address); the mail is sent off the request path.
    /// </summary>
    public async Task<ScreenResult<ForgotPasswordViewModel>> ResendConfirmationAsync(ForgotPasswordForm? form, string? clientAddress)
    {
        var model = new ForgotPasswordViewModel { Identifier = form?.Identifier };
        var errors = ScreenValidator.Validate(form);
        if (errors.Count > 0) return ScreenResult<ForgotPasswordViewModel>.Invalid(model, errors);

        var identifier = form!.Identifier?.Trim();

        #pragma warning disable CS8604
        var byName = await _users.FindByNameAsync(identifier);
        #pragma warning restore CS8604
        BaGetUser? byEmail = null;
        try
        {
            byEmail = await _users.FindByEmailAsync(identifier);
        }
        catch (InvalidOperationException)
        {
            // Several accounts share the address: none is singled out.
        }

        var user = byName ?? byEmail;
        if (user != null
            && !user.EmailConfirmed
            && !string.IsNullOrEmpty(user.Email)
            && EmailConfigured
            && _services.GetRequiredService<PasswordResetRateLimiter>().TryAcquire(clientAddress ?? string.Empty, "resend:" + user.Id))
        {
            var userId = user.Id;
            _services.GetRequiredService<BackgroundEmailQueue>().TryEnqueue(async services =>
            {
                var users = services.GetRequiredService<UserManager<BaGetUser>>();
                var account = await users.FindByIdAsync(userId);
                if (account == null || account.EmailConfirmed || string.IsNullOrEmpty(account.Email)) return;

                var token = await users.GenerateEmailConfirmationTokenAsync(account);
                var link = AccountLinks.ConfirmEmail(services.GetRequiredService<IOptions<SiteOptions>>(), account.Id, token);
                await services.GetRequiredService<IEmailSender<BaGetUser>>().SendConfirmationLinkAsync(account, account.Email, link);
            });
        }

        model.Sent = true;

        return ScreenResult<ForgotPasswordViewModel>.Ok(model);
    }

    // ----- the confirmation link

    /// <summary>
    /// The target of the links in confirmation emails: a new account's address, or, when <paramref name="email"/> is present, a change of
    /// address. The link is followed with a GET, so this changes state on a safe request, as the page always did (the token is the secret).
    /// </summary>
    public async Task<ScreenResult<ConfirmEmailViewModel>> ConfirmEmailAsync(System.Security.Claims.ClaimsPrincipal caller, string? userId, string? code, string? email)
    {
        var model = new ConfirmEmailViewModel();

        var user = string.IsNullOrEmpty(userId) ? null : await _users.FindByIdAsync(userId);
        var token = string.IsNullOrEmpty(code) ? null : AccountConfirmation.DecodeToken(code);
        if (user == null || token == null) return ScreenResult<ConfirmEmailViewModel>.Ok(model);

        IdentityResult result;
        if (string.IsNullOrEmpty(email))
        {
            result = await _users.ConfirmEmailAsync(user, token);
        }
        else
        {
            result = await _users.ChangeEmailAsync(user, email, token);
            model.EmailChanged = result.Succeeded;
        }

        model.Succeeded = result.Succeeded;
        model.PendingApproval = model.Succeeded && user.PendingApproval;

        // The security stamp changed with the address; keep the session of the same user.
        if (model.Succeeded && caller.Identity?.IsAuthenticated == true && _users.GetUserId(caller) == user.Id)
        {
            await _signIn.RefreshSignInAsync(user);
        }

        return ScreenResult<ConfirmEmailViewModel>.Ok(model);
    }

    // ----- forgot password

    /// <summary>The "forgot password" form.</summary>
    public ScreenResult<ForgotPasswordViewModel> ShowForgotPassword() => ScreenResult<ForgotPasswordViewModel>.Ok(new ForgotPasswordViewModel());

    /// <summary>
    /// Answers the same, in the same time, whether or not an account matches, whether or not the mail could be sent and whether or not the
    /// request was rate limited, so the screen cannot be used to find out who has an account. The mail itself is sent off the request path.
    /// </summary>
    /// <param name="form">The user name or email address.</param>
    /// <param name="clientAddress">The caller's address, for the rate limit (empty when unknown).</param>
    public async Task<ScreenResult<ForgotPasswordViewModel>> ForgotPasswordAsync(ForgotPasswordForm? form, string? clientAddress)
    {
        var model = new ForgotPasswordViewModel { Identifier = form?.Identifier };
        var errors = ScreenValidator.Validate(form);
        if (errors.Count > 0) return ScreenResult<ForgotPasswordViewModel>.Invalid(model, errors);

        var identifier = form!.Identifier?.Trim();

        // Both lookups always run, so the work done does not depend on which one matches.
        #pragma warning disable CS8604
        var byName = await _users.FindByNameAsync(identifier);
        #pragma warning restore CS8604
        BaGetUser? byEmail = null;
        try
        {
            byEmail = await _users.FindByEmailAsync(identifier);
        }
        catch (InvalidOperationException)
        {
            // Several accounts share the address: none is singled out.
        }

        var user = byName ?? byEmail;

        // One link template either way; the user and token are filled in later.
        var template = AccountLinks.ResetPasswordTemplate(_site);

        // Only an account that would be mailed uses up an allowance, so requests for names
        // that do not exist can neither fill the limiter nor crowd real accounts out of it.
        // The per-account limit is the protection; the client and site limits are backstops.
        if (user != null
            && !string.IsNullOrEmpty(user.Email)
            && _services.GetRequiredService<PasswordResetRateLimiter>().TryAcquire(clientAddress ?? string.Empty, user.Id))
        {
            PasswordResetLinkSender.TryEnqueue(_services, user.Id, template);
        }

        model.Sent = true;

        return ScreenResult<ForgotPasswordViewModel>.Ok(model);
    }

    // ----- the new-password form

    /// <summary>The link of a reset email was followed.</summary>
    public ScreenResult<ResetPasswordViewModel> ShowResetPassword(string? userId, string? code)
    {
        var model = new ResetPasswordViewModel { UserId = userId, Code = code };
        if (!model.LinkIsUsable) model.Error = "This link is invalid.";

        return ScreenResult<ResetPasswordViewModel>.Ok(model);
    }

    /// <summary>The new password was posted. Success continues at the sign-in screen.</summary>
    public async Task<ScreenResult<ResetPasswordViewModel>> ResetPasswordAsync(ResetPasswordForm? form)
    {
        var model = new ResetPasswordViewModel { UserId = form?.UserId, Code = form?.Code };

        var errors = ScreenValidator.Validate(form);
        if (errors.Count > 0) return ScreenResult<ResetPasswordViewModel>.Invalid(model, errors);

        #pragma warning disable CS8604
        var user = form!.LinkIsUsable ? await _users.FindByIdAsync(form.UserId) : null;
        #pragma warning restore CS8604
        var token = form.LinkIsUsable ? AccountConfirmation.DecodeToken(form.Code) : null;
        if (user == null || token == null)
        {
            return Refuse(model, InvalidLink);
        }

        #pragma warning disable CS8604
        var result = await _users.ResetPasswordAsync(user, token, form.Password);
        #pragma warning restore CS8604
        if (!result.Succeeded)
        {
            return Refuse(
                model,
                result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                    ? InvalidLink
                    : AccountConfirmation.Describe(result));
        }

        // The link went to this address and was followed, which confirms it.
        if (!user.EmailConfirmed && !string.IsNullOrEmpty(user.Email))
        {
            await _users.ConfirmEmailAsync(user, await _users.GenerateEmailConfirmationTokenAsync(user));
        }

        return ScreenResult<ResetPasswordViewModel>.GoTo(new ScreenNext(ScreenCatalog.AccountLogin));
    }

    private static ScreenResult<ResetPasswordViewModel> Refuse(ResetPasswordViewModel model, string message)
    {
        model.Error = message;

        return ScreenResult<ResetPasswordViewModel>.Invalid(model, message);
    }
}
