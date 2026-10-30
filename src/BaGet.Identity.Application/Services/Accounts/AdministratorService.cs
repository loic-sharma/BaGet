using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Reads and changes <see cref="BaGetUser.IsAdministrator"/>. Read from the database on
/// every check, so a demotion takes effect on the next request.
/// </summary>
public class AdministratorService
{
    /// <summary>The administrator <see cref="CreateDefaultAdministratorAsync"/> creates on a server with no users.</summary>
    public const string DefaultAdministratorUserName = "Admin";

    /// <summary>
    /// The default administrator's initial password. It is public knowledge, so it must be changed
    /// right after the first sign-in; every startup warns while it still works.
    /// </summary>
    public const string DefaultAdministratorPassword = "Admin1234!";

    private readonly UserManager<BaGetUser> _users;
    private readonly IContext _context;
    private readonly SystemTime _time;
    private readonly ILogger<AdministratorService> _logger;

    /// <summary>Creates the service.</summary>
    public AdministratorService(UserManager<BaGetUser> users, IContext context, SystemTime time, ILogger<AdministratorService> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// On a server with no users at all, create the administrator <see cref="DefaultAdministratorUserName"/>
    /// with the password <see cref="DefaultAdministratorPassword"/>, so that a fresh install can be signed in to
    /// and configured. Its email counts as confirmed, so it can sign in while SMTP is configured, although it has no
    /// address. Once any user exists this does nothing, even if that user is later deleted.
    /// </summary>
    /// <returns>Whether the administrator was created.</returns>
    public async Task<bool> CreateDefaultAdministratorAsync(CancellationToken cancellationToken)
    {
        if (await _context.Users.AnyAsync(cancellationToken)) return false;

        var user = new BaGetUser
        {
            UserName = DefaultAdministratorUserName,
            EmailConfirmed = true,
            IsAdministrator = true,
            Created = _time.UtcNow,
        };

        var result = await _users.CreateAsync(user, DefaultAdministratorPassword);

        // Several instances starting at once on an empty database may race; the unique user name lets
        // only one of them create the account, and the others have nothing left to do.
        if (!result.Succeeded && result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName))) return false;
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        _logger.LogWarning(
            "There were no users, so the administrator {User} was created with the default password. Sign in and change it now.",
            DefaultAdministratorUserName);

        return true;
    }

    /// <summary>Whether <see cref="DefaultAdministratorUserName"/> exists and still has the default password.</summary>
    public async Task<bool> HasDefaultPasswordAsync()
    {
        var user = await _users.FindByNameAsync(DefaultAdministratorUserName);

        return user != null && await _users.CheckPasswordAsync(user, DefaultAdministratorPassword);
    }

    /// <summary>Whether the signed in user is an administrator, according to the database.</summary>
    public async Task<bool> IsAdministratorAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var id = principal == null ? null : _users.GetUserId(principal);

        return !string.IsNullOrEmpty(id)
            && await _context.Users.AnyAsync(u => u.Id == id && u.IsAdministrator, cancellationToken);
    }

    /// <summary>Makes the user an administrator, and approves the account if it was waiting for approval.</summary>
    /// <param name="userName">The user to promote.</param>
    /// <param name="actor">Who asks, for the log.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<AdministratorChange> PromoteAsync(string? userName, string? actor, CancellationToken cancellationToken)
        => SetAsync(userName, true, actor, cancellationToken);

    /// <summary>Removes the user's administrator flag, unless they are the last administrator.</summary>
    /// <param name="userName">The user to demote.</param>
    /// <param name="actor">Who asks, for the log.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<AdministratorChange> DemoteAsync(string? userName, string? actor, CancellationToken cancellationToken)
        => SetAsync(userName, false, actor, cancellationToken);

    /// <summary>Whether the user is an administrator and no other administrator exists.</summary>
    public async Task<bool> IsLastAdministratorAsync(BaGetUser? user, CancellationToken cancellationToken)
    {
        if (user == null || !user.IsAdministrator) return false;

        return await _context.Users.CountAsync(u => u.IsAdministrator, cancellationToken) <= 1;
    }

    /// <summary>
    /// Bootstraps a server without administrators: promotes the semicolon separated user
    /// names, but only while no administrator exists. Once one does, it does nothing, so a
    /// listed name registered later is not promoted and a demotion is not reversed at restart.
    /// A name with no account is logged and skipped.
    /// </summary>
    public async Task PromoteInitialAdministratorsAsync(string? names, CancellationToken cancellationToken)
    {
        if (await _context.Users.AnyAsync(u => u.IsAdministrator, cancellationToken)) return;

        var separators = new[] { ';' };
        foreach (var name in (names ?? string.Empty).Split(separators, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            if (await SetAsync(name, true, "configuration", cancellationToken) == AdministratorChange.UserNotFound)
            {
                _logger.LogWarning("Initial administrator {User} does not exist and was not promoted", name);
            }
        }
    }

    private async Task<AdministratorChange> SetAsync(string? userName, bool value, string? actor, CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _users.FindByNameAsync(userName.Trim());
        if (user == null) return AdministratorChange.UserNotFound;
        // An administrator is by definition approved: promotion also enables an account that waits for approval
        // (Accounts:RequireApproval), else a recovery could leave the only administrator unable to sign in.
        var approves = value && user.PendingApproval;
        if (user.IsAdministrator == value && !approves) return AdministratorChange.Unchanged;
        if (!value && await IsLastAdministratorAsync(user, cancellationToken)) return AdministratorChange.LastAdministrator;

        user.IsAdministrator = value;
        if (value) user.PendingApproval = false;
        var result = await _users.UpdateAsync(user); // the concurrency stamp rejects a racing edit of the same user
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        _logger.LogWarning("{Actor} {Action} {User} as administrator", actor, value ? "promoted" : "demoted", user.UserName);

        return AdministratorChange.Changed;
    }
}
