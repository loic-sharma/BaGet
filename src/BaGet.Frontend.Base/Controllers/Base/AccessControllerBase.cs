using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Frontend.Mvc.Screens;
using BaGet.Identity.Application.Models.Policies;
using BaGet.Identity.Application.Models.Shared;
using BaGet.Identity.Application.Screens;
using BaGet.Identity.Application.Services.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of the access-control screens (groups, permission targets, projects, audit). Signed in, accounts enabled, an administrator, unless a screen widens it to a project's administrators
/// (<see cref="AuthorizeAsync"/>); anyone else gets 404. Needs a relational database: where there is no access model (Azure
/// Table) every screen sends an administrator to <c>/admin/access-unavailable</c>, which says so. Every POST is refused with 503
/// while the <see cref="MaintenanceGate"/> is closed, and changes go through <see cref="IAccessAdministration"/>, which audits
/// them and invalidates the evaluator's cache. Retention and replication screens build on it
/// (<see cref="RetentionControllerBase"/>, <see cref="ReplicationControllerBase"/>).
/// </summary>
public abstract class AccessControllerBase : AdminControllerBase
{
    /// <summary>Outcome of the last action.</summary>
    [TempData]
    public string? Message { get; set; }

    /// <summary>Why the last action was refused.</summary>
    [TempData]
    public string? Error { get; set; }

    /// <summary>Whether the signed-in user is a server administrator (rather than only a project administrator).</summary>
    public bool IsServerAdministrator { get; private set; }

    /// <summary>The signed-in user's id.</summary>
    protected string? CurrentUserId { get; private set; }

    /// <summary>Whether the signed in user has a password to confirm with.</summary>
    public bool HasPassword { get; private set; } = true;

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; private set; }

    /// <summary>Whether an external provider linked to the signed-in account is available to sign in again with.</summary>
    public bool CanReauthenticate { get; private set; }

    /// <summary>The access model's administration, resolved per request.</summary>
    protected IAccessAdministration AccessAdmin => HttpContext.RequestServices.GetRequiredService<IAccessAdministration>();

    /// <summary>The access evaluator, resolved per request.</summary>
    protected IAccessEvaluator Evaluator => HttpContext.RequestServices.GetRequiredService<IAccessEvaluator>();

    /// <summary>The user name recorded in the audit log.</summary>
    protected string? Actor => User.Identity?.Name;

    /// <summary>The model of the password confirmation partial, with <paramref name="id"/> for the input.</summary>
    [NonAction]
    public ConfirmPasswordView Confirmation(string id)
        => new(id, HasPassword, Reauthenticated, CanReauthenticate, "~" + Request.Path + Request.QueryString);

    /// <summary>
    /// Whether the signed-in user may use this screen. The default is server administrators; project screens also admit the
    /// administrators of the project they act on.
    /// </summary>
    protected virtual async Task<bool> AuthorizeAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return IsServerAdministrator;
    }

    /// <inheritdoc />
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Not AdminControllerBase's own check: a project administrator is admitted by AuthorizeAsync.
        OnActionExecuting(context);
        if (context.Result != null) return;

        var cancellationToken = HttpContext.RequestAborted;
        CurrentUserId = UserManager.GetUserId(User);

        // The gate holds the order: administrator or whoever AuthorizeAsync admits, the access model present, writable on a POST.
        var gate = await Gate.CheckAsync(
            User,
            new GateRequest(Role: GateRole.AccessAdministrator, IsWrite: HttpMethods.IsPost(Request.Method), AlsoAllow: async ct => await AuthorizeAsync(ct)),
            cancellationToken);
        IsServerAdministrator = gate.IsServerAdministrator;

        if (gate.Outcome == GateOutcome.AccessUnavailable)
        {
            context.Result = RedirectToAction(ScreenCatalog.AdminAccessUnavailable.Action, ScreenCatalog.AdminAccessUnavailable.Controller);
            return;
        }

        if (!gate.IsAllowed)
        {
            context.Result = NotFound();
            return;
        }

        var current = await UserManager.GetUserAsync(User);
        HasPassword = current == null || await UserManager.HasPasswordAsync(current);
        Reauthenticated = !HasPassword && await HasReauthenticatedRecentlyAsync();
        CanReauthenticate = await HttpContext.RequestServices.GetRequiredService<ExternalLogins>().CanReauthenticateAsync(current);

        OnActionExecuted(await next());
    }

    /// <summary>
    /// Runs one change: asks for the password when <paramref name="confirm"/>, turns an <see cref="AccessModelException"/> into
    /// <see cref="Error"/>, and redirects to <paramref name="redirect"/>.
    /// </summary>
    /// <param name="password">What the user typed.</param>
    /// <param name="confirm">Whether the change asks for the password (grants, memberships, deletions).</param>
    /// <param name="change">The change; returns the message to show.</param>
    /// <param name="redirect">Where to go afterwards.</param>
    protected async Task<IActionResult> ActAsync(string? password, bool confirm, Func<Task<string>> change, Func<IActionResult> redirect)
    {
        if (confirm)
        {
            var current = await UserManager.GetUserAsync(User);
            if (current == null) return Challenge();

            if (!await IsConfirmedAsync(current, password))
            {
                Error = "The confirmation is incorrect, so nothing was changed.";
                return redirect();
            }
        }

        try
        {
            Message = await change();
        }
        catch (AccessModelException e)
        {
            Error = e.Message;
        }

        return redirect();
    }

    /// <summary>
    /// What a set of patterns selects, for the pattern editors: the repositories whose names match the repository patterns, and,
    /// when a package ID was typed in the test box, whether the target would apply to it.
    /// </summary>
    protected Task<PatternPreview> PreviewAsync(
        string[] repositoryPatterns,
        string[] includePatterns,
        string[] excludePatterns,
        string? testPackage,
        CancellationToken cancellationToken)
        => AccessSupport.PreviewAsync(HttpContext.RequestServices, repositoryPatterns, includePatterns, excludePatterns, testPackage, cancellationToken);

    /// <summary>Parses the ticked action boxes (names of <see cref="AccessAction"/> values).</summary>
    protected static AccessAction ParseActions(IEnumerable<string>? names) => AccessSupport.ParseActions(names);

    /// <summary>The single actions, in display order.</summary>
    public static AccessAction[] AllActions => AccessSupport.AllActions;

    /// <summary>The actions in <paramref name="actions"/> as words, or "none".</summary>
    public static string Words(AccessAction actions) => AccessSupport.Words(actions);

    /// <summary>Patterns one per line, the way the editors show them.</summary>
    public static string Lines(IEnumerable<string> patterns) => AccessSupport.Lines(patterns);

    /// <summary>The package filter and repository patterns as one line a person can read back.</summary>
    public static string Join(IEnumerable<string> patterns) => AccessSupport.Join(patterns);
}
