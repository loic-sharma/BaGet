using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// The check behind every sensitive action of the account and administration screens: the caller types their password again, or, for an
/// account without one (external sign-in only), has signed in again with the provider a moment ago. Also fills the confirmation fields
/// (<see cref="ConfirmationModel"/>) a screen needs to ask for it. The only part of a use case that reads the request is the re-authentication
/// time, which lives in the session cookie, so it is read here (through <see cref="IHttpContextAccessor"/>) and never by the use case itself.
/// </summary>
public sealed class PasswordConfirmation
{
    /// <summary>How long a sign-in with the external provider counts as proof of presence for a sensitive action.</summary>
    public static readonly TimeSpan ReauthenticationWindow = AccountConfirmation.ReauthenticationWindow;

    /// <summary>The sign-in cookie property that holds the time (ticks, UTC) of the last re-authentication.</summary>
    public const string ReauthenticatedItem = AccountConfirmation.ReauthenticatedItem;

    private readonly UserManager<BaGetUser> _users;
    private readonly IHttpContextAccessor _http;
    private readonly ExternalLogins _logins;

    /// <summary>Creates the service.</summary>
    public PasswordConfirmation(UserManager<BaGetUser> users, IHttpContextAccessor http, IOptions<AccountsOptions> accounts)
        : this(users, http, accounts, ExternalLogins.For(users, accounts))
    {
    }

    /// <summary>Creates the service.</summary>
    public PasswordConfirmation(UserManager<BaGetUser> users, IHttpContextAccessor http, IOptions<AccountsOptions> accounts, ExternalLogins logins)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _logins = logins ?? throw new ArgumentNullException(nameof(logins));
    }

    /// <summary>Whether <paramref name="user"/> confirmed: the right password, or, for an account without one, a recent provider sign-in.</summary>
    public Task<bool> IsConfirmedAsync(BaGetUser? user, string? password)
        => IsConfirmedAsync(_users, _http.HttpContext, user, password);

    /// <summary>Fills the fields the confirmation partial needs for <paramref name="caller"/>.</summary>
    /// <param name="model">The screen's model.</param>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="returnPage">The page to come back to after signing in again, or null to leave the model's.</param>
    public async Task LoadAsync(ConfirmationModel model, ClaimsPrincipal caller, string? returnPage = null)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var current = await _users.GetUserAsync(caller);
        model.HasPassword = current == null || await _users.HasPasswordAsync(current);
        model.Reauthenticated = !model.HasPassword && await HasReauthenticatedRecentlyAsync(_http.HttpContext);
        model.CanReauthenticate = await _logins.CanReauthenticateAsync(current);
        if (returnPage != null) model.ReturnPage = returnPage;
    }

    /// <summary>
    /// <see cref="LoadAsync"/> with the page to come back to after signing in again being the URL (path and query, application-relative with <c>~</c> so a path base is kept) of the request being served, as the
    /// access screens always did. Through the in-process call of a view controller that is the screen's own address.
    /// </summary>
    public Task LoadForRequestAsync(ConfirmationModel model, ClaimsPrincipal caller)
    {
        var request = _http.HttpContext?.Request;

        return LoadAsync(model, caller, request == null ? null : "~" + request.Path + request.QueryString);
    }

    /// <summary>The check behind every sensitive action, for a caller that holds its own <see cref="HttpContext"/> (the rule lives in <see cref="AccountConfirmation"/>).</summary>
    public static Task<bool> IsConfirmedAsync(UserManager<BaGetUser> users, HttpContext? httpContext, BaGetUser? user, string? password)
        => AccountConfirmation.IsConfirmedAsync(users, httpContext!, user, password);

    /// <summary>Whether the session re-authenticated with the external provider within <see cref="ReauthenticationWindow"/>.</summary>
    public static Task<bool> HasReauthenticatedRecentlyAsync(HttpContext? httpContext)
        => httpContext == null ? Task.FromResult(false) : AccountConfirmation.HasReauthenticatedRecentlyAsync(httpContext);
}
