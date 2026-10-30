using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Enables the accounts that wait for an administrator (<see cref="BaGetUser.PendingApproval"/>, set for self-registered
/// accounts while <c>Accounts:RequireApproval</c> is on). Rejecting an account is deleting it (the Users page's Delete).
/// </summary>
public class AccountApprovalService
{
    private readonly UserManager<BaGetUser> _users;
    private readonly IContext _context;
    private readonly ILogger<AccountApprovalService> _logger;

    /// <summary>Creates the service.</summary>
    public AccountApprovalService(UserManager<BaGetUser> users, IContext context, ILogger<AccountApprovalService> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The number of accounts waiting for approval.</summary>
    public Task<int> CountPendingAsync(CancellationToken cancellationToken)
        => _context.Users.CountAsync(u => u.PendingApproval, cancellationToken);

    /// <summary>Enables the user, who can then sign in. A racing delete or edit of the same user is <see cref="AccountApprovalResult.Conflict"/>.</summary>
    /// <param name="userName">The user to enable.</param>
    /// <param name="actor">Who asks, for the log.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<AccountApprovalResult> ApproveAsync(string? userName, string? actor, CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _users.FindByNameAsync(userName.Trim());
        if (user == null) return AccountApprovalResult.UserNotFound;
        if (!user.PendingApproval) return AccountApprovalResult.NotPending;

        user.PendingApproval = false;
        var result = await _users.UpdateAsync(user); // the concurrency stamp rejects a racing edit of the same user
        if (!result.Succeeded)
        {
            // A racing delete or edit (concurrency failure): the account is gone or different, so report it, don't crash.
            _logger.LogWarning("{Actor} could not approve {User}: {Errors}", actor, user.UserName, string.Join(" ", result.Errors.Select(e => e.Code)));

            return AccountApprovalResult.Conflict;
        }

        _logger.LogWarning("{Actor} approved the account {User}", actor, user.UserName);

        return AccountApprovalResult.Approved;
    }
}
