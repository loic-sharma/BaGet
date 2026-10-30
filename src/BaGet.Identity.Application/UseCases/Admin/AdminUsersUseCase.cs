using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.Models.Users;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// The Users screen: lists the accounts and lets an administrator approve, promote or demote them, create an account, reset a
/// password (a generated one shown once, or a link mailed to the user). Every action asks for the administrator's password again,
/// and none is offered or accepted for the administrator's own account (that stays on <c>/account/settings</c>).
/// </summary>
public sealed class AdminUsersUseCase : AdminUseCaseBase
{
    private static readonly ScreenRef Screen = ScreenCatalog.AdminUsers;

    private readonly IContext _context;
    private readonly AvatarService _avatars;
    private readonly AccountApprovalService _approvals;
    private readonly AccountRecoveryService _recovery;
    private readonly AdminUserCreationService _creation;
    private readonly AdministratorService _administrators;
    private readonly IOptions<EmailOptions> _email;
    private readonly IScreenLinks _links;
    private readonly IServiceProvider _services;
    private readonly ILogger<AdminUsersUseCase> _logger;

    /// <summary>Creates the use case.</summary>
    public AdminUsersUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        IContext context,
        AvatarService avatars,
        AccountApprovalService approvals,
        AccountRecoveryService recovery,
        AdminUserCreationService creation,
        AdministratorService administrators,
        IOptions<EmailOptions> email,
        IScreenLinks links,
        IServiceProvider services,
        ILogger<AdminUsersUseCase> logger,
        MaintenanceGate? gate = null)
        : base(users, confirmation, gate)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _creation = creation ?? throw new ArgumentNullException(nameof(creation));
        _administrators = administrators ?? throw new ArgumentNullException(nameof(administrators));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private bool EmailConfigured => _email.Value.IsConfigured;

    /// <summary>Lists the users.</summary>
    public async Task<ScreenResult<UsersViewModel>> GetAsync(ClaimsPrincipal caller, string? q, int pageNumber, CancellationToken cancellationToken)
    {
        var model = new UsersViewModel { Q = q, PageNumber = pageNumber };
        await LoadAsync(model, caller, cancellationToken);

        return ScreenResult<UsersViewModel>.Ok(model);
    }

    /// <summary>Enables a self-registered account that waits for approval. Asks the password; 503 while the gate is closed.</summary>
    public async Task<ScreenResult<UsersViewModel>> ApproveAsync(ClaimsPrincipal caller, UserChange change, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<UsersViewModel>.SignIn();

        string? message = null, error = null;
        if (!await Confirmation.IsConfirmedAsync(current, change.Password))
        {
            error = NotConfirmed;
        }
        else
        {
            var userName = change.UserName!;
            switch (await _approvals.ApproveAsync(userName, current.UserName, cancellationToken))
            {
                case AccountApprovalResult.Approved:
                    message = $"{userName} was approved and can sign in.";
                    break;
                case AccountApprovalResult.NotPending:
                    message = $"{userName} was not waiting for approval.";
                    break;
                case AccountApprovalResult.Conflict:
                    error = $"{userName} was changed or removed meanwhile, so nothing was approved. Reload the list.";
                    break;
                default:
                    error = "There is no user with that name.";
                    break;
            }
        }

        return BackToUsers(change.Q, change.PageNumber, message, error);
    }

    /// <summary>Makes a user an administrator.</summary>
    public Task<ScreenResult<UsersViewModel>> PromoteAsync(ClaimsPrincipal caller, UserChange change, CancellationToken cancellationToken)
        => ChangeAsync(caller, change, promote: true, cancellationToken);

    /// <summary>Removes a user's administrator flag.</summary>
    public Task<ScreenResult<UsersViewModel>> DemoteAsync(ClaimsPrincipal caller, UserChange change, CancellationToken cancellationToken)
        => ChangeAsync(caller, change, promote: false, cancellationToken);

    /// <summary>
    /// Replaces the user's password with a generated one and shows it once. The answer is the screen itself (Ok with
    /// <see cref="UsersViewModel.GeneratedPassword"/>) rather than a redirect, so the password never goes through a flash message or a cookie; the
    /// caller must make the response uncacheable.
    /// </summary>
    public async Task<ScreenResult<UsersViewModel>> ResetPasswordAsync(ClaimsPrincipal caller, UserChange change, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<UsersViewModel>.SignIn();

        var refusal = await RefuseAsync(current, change.UserName, change.Password);
        if (refusal != null) return BackToUsers(change.Q, change.PageNumber, null, refusal);

        if (string.IsNullOrWhiteSpace(change.UserName)) return BackToUsers(change.Q, change.PageNumber, null, "There is no user with that name.");

        var generated = await _recovery.ResetPasswordAsync(change.UserName!, cancellationToken, change.ResetEmail);
        if (generated == null) return BackToUsers(change.Q, change.PageNumber, null, "There is no user with that name.");

        _logger.LogWarning("{Admin} reset the password of {User}", current.UserName, change.UserName);

        var model = new UsersViewModel
        {
            Q = change.Q,
            PageNumber = change.PageNumber,
            GeneratedPassword = generated,
            GeneratedFor = change.UserName,
        };
        await LoadAsync(model, caller, cancellationToken);

        return ScreenResult<UsersViewModel>.Ok(model);
    }

    /// <summary>Mails the user a password reset link. Only while SMTP is configured, and only to a user with an address.</summary>
    public async Task<ScreenResult<UsersViewModel>> SendResetLinkAsync(ClaimsPrincipal caller, UserChange change, CancellationToken cancellationToken)
    {
        if (!EmailConfigured) return ScreenResult<UsersViewModel>.NotFound();

        ThrowIfClosed();
        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<UsersViewModel>.SignIn();

        var refusal = await RefuseAsync(current, change.UserName, change.Password);
        if (refusal != null) return BackToUsers(change.Q, change.PageNumber, null, refusal);

        string? message = null, error = null;
        var user = string.IsNullOrWhiteSpace(change.UserName) ? null : await Users.FindByNameAsync(change.UserName);
        if (user == null)
        {
            error = "There is no user with that name.";
        }
        else if (string.IsNullOrEmpty(user.Email))
        {
            error = $"{user.UserName} has no email address, so no link can be sent.";
        }
        else
        {
            // No rate limiter: an administrator acts, and no one can use this to probe for accounts.
            var template = _links.AbsoluteLink(ScreenCatalog.AccountResetPassword, PasswordResetLinkSender.Template);
            if (PasswordResetLinkSender.TryEnqueue(_services, user.Id, template))
            {
                _logger.LogWarning("{Admin} sent a password reset link to {User}", current.UserName, user.UserName);
                message = $"Reset link sent to {UsersViewModel.MaskEmail(user.Email)}.";
            }
            else
            {
                error = "The mail queue is full. Try again in a moment.";
            }
        }

        return BackToUsers(change.Q, change.PageNumber, message, error);
    }

    /// <summary>
    /// Creates an account. The password is typed, generated (shown once on this response, like a reset) or, with
    /// <see cref="UserCreation.Invite"/>, unknown to everyone while the user is mailed a link to choose their own. Asks for the
    /// administrator's password; refused with 503 while the <see cref="MaintenanceGate"/> is closed.
    /// </summary>
    public async Task<ScreenResult<UsersViewModel>> CreateAsync(ClaimsPrincipal caller, UserCreation creation, CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<UsersViewModel>.SignIn();

        if (!await Confirmation.IsConfirmedAsync(current, creation.Password))
        {
            return BackToUsers(creation.Q, creation.PageNumber, null, NotConfirmed);
        }

        if (creation.Invite && (!EmailConfigured || string.IsNullOrWhiteSpace(creation.NewEmail)))
        {
            return BackToUsers(
                creation.Q,
                creation.PageNumber,
                null,
                EmailConfigured
                    ? "An invitation is mailed to the user, so an email address is required."
                    : "Email is not configured, so no invitation can be sent. Give a password or generate one.");
        }

        var mode = creation.Invite
            ? NewUserPasswordMode.Invited
            : string.IsNullOrEmpty(creation.NewPassword) ? NewUserPasswordMode.Generated : NewUserPasswordMode.Given;
        var created = await _creation.CreateAsync(creation.NewUserName, creation.NewEmail, mode, creation.NewPassword, cancellationToken);
        if (!created.Succeeded) return BackToUsers(creation.Q, creation.PageNumber, null, created.Error);

        _logger.LogWarning("{Admin} created the user {User}", current.UserName, created.User!.UserName);

        if (mode == NewUserPasswordMode.Generated)
        {
            var model = new UsersViewModel
            {
                Q = creation.Q,
                PageNumber = creation.PageNumber,
                GeneratedPassword = created.GeneratedPassword,
                GeneratedFor = created.User.UserName,
                GeneratedForNewUser = true,
            };
            await LoadAsync(model, caller, cancellationToken);

            return ScreenResult<UsersViewModel>.Ok(model);
        }

        string message;
        if (mode == NewUserPasswordMode.Invited)
        {
            var template = _links.AbsoluteLink(ScreenCatalog.AccountResetPassword, PasswordResetLinkSender.Template);
            message = PasswordResetLinkSender.TryEnqueue(_services, created.User.Id, template)
                ? $"{created.User.UserName} was created. An invitation was sent to {UsersViewModel.MaskEmail(created.User.Email)}."
                : $"{created.User.UserName} was created, but the mail queue is full, so no invitation was sent. Use Reset password, Email a reset link.";
        }
        else
        {
            message = $"{created.User.UserName} was created.";
        }

        return BackToUsers(creation.Q, creation.PageNumber, message, null);
    }

    // Why a reset may not go ahead, or null.
    private async Task<string?> RefuseAsync(BaGetUser current, string? userName, string? password)
    {
        if (string.Equals(current.UserName, userName, StringComparison.OrdinalIgnoreCase))
        {
            return "Reset your own password from your account settings.";
        }

        return await Confirmation.IsConfirmedAsync(current, password) ? null : NotConfirmed;
    }

    private static ScreenResult<UsersViewModel> BackToUsers(string? q, int pageNumber, string? message, string? error)
        => Back<UsersViewModel>(Screen, Route(("q", q), ("p", pageNumber > 1 ? pageNumber : (int?)null)), message, error);

    private async Task<ScreenResult<UsersViewModel>> ChangeAsync(ClaimsPrincipal caller, UserChange change, bool promote, CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        var current = await CurrentAsync(caller);
        if (current == null) return ScreenResult<UsersViewModel>.SignIn();

        string? message = null, error = null;
        if (!await Confirmation.IsConfirmedAsync(current, change.Password))
        {
            error = NotConfirmed;
        }
        else
        {
            var userName = change.UserName!;
            var result = promote
                ? await _administrators.PromoteAsync(userName, current.UserName, cancellationToken)
                : await _administrators.DemoteAsync(userName, current.UserName, cancellationToken);

            switch (result)
            {
                case AdministratorChange.Changed:
                    message = promote ? $"{userName} is now an administrator." : $"{userName} is no longer an administrator.";
                    break;
                case AdministratorChange.Unchanged:
                    message = promote ? $"{userName} is already an administrator." : $"{userName} is not an administrator.";
                    break;
                case AdministratorChange.UserNotFound:
                    error = "There is no user with that name.";
                    break;
                case AdministratorChange.LastAdministrator:
                    error = "The last administrator cannot be demoted. Promote another user first.";
                    break;
            }
        }

        return BackToUsers(change.Q, change.PageNumber, message, error);
    }

    private async Task LoadAsync(UsersViewModel model, ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var query = _context.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(model.Q))
        {
            var term = model.Q.Trim().ToLowerInvariant();
            #pragma warning disable CS8602
            query = query.Where(u => u.UserName.ToLower().Contains(term));
            #pragma warning restore CS8602
        }

        var total = await query.CountAsync(cancellationToken);
        model.PageCount = Math.Max(1, (total + UsersViewModel.PageSize - 1) / UsersViewModel.PageSize);
        model.PageNumber = Math.Min(Math.Max(1, model.PageNumber), model.PageCount);

        var page = await query
            .OrderBy(u => u.UserName)
            .Skip((model.PageNumber - 1) * UsersViewModel.PageSize)
            .Take(UsersViewModel.PageSize)
            .Select(u => new { u.UserName, u.Email, u.Created, u.IsAdministrator, u.PendingApproval })
            .ToListAsync(cancellationToken);

        var avatars = await _avatars.GetUrlsAsync(page.Select(u => u.UserName).ToList(), 24, cancellationToken);

        #pragma warning disable CS8620
        model.Users = page
            .Select(u => new UserRow(u.UserName, u.Email, u.Created, u.IsAdministrator, avatars.GetValueOrDefault(u.UserName), u.PendingApproval))
            .ToList();
        #pragma warning restore CS8620

        model.PendingCount = await _approvals.CountPendingAsync(cancellationToken);
        model.CanEmailResetLink = EmailConfigured;
        model.CanInvite = EmailConfigured;

        model.CurrentUserName = (await CurrentAsync(caller))?.UserName;
        await Confirmation.LoadAsync(model, caller);
    }
}
