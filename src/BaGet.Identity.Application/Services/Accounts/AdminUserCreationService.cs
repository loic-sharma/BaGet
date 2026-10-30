using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Creates an account on an administrator's behalf. The same name rule as registration applies (user and
/// organization names share one namespace); the address is trusted, as in
/// <see cref="AccountRecoveryService"/>, so it is marked confirmed and the user can sign in while SMTP is on.
/// </summary>
public class AdminUserCreationService
{
    private readonly UserManager<BaGetUser> _users;
    private readonly PackageOwnershipService _ownership;
    private readonly SystemTime _time;

    /// <summary>Creates the service.</summary>
    public AdminUserCreationService(UserManager<BaGetUser> users, PackageOwnershipService ownership, SystemTime time)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Create the user, or say why not.</summary>
    /// <param name="userName">The user name.</param>
    /// <param name="email">The address, or blank for none.</param>
    /// <param name="mode">Where the first password comes from.</param>
    /// <param name="password">The typed password, for <see cref="NewUserPasswordMode.Given"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<NewUserResult> CreateAsync(
        string? userName,
        string? email,
        NewUserPasswordMode mode,
        string? password,
        CancellationToken cancellationToken)
    {
        var name = userName?.Trim();
        if (string.IsNullOrEmpty(name)) return new NewUserResult(null, "A user name is required.");
        if (name.Length > AbstractContext<DbContext>.MaxAccountNameLength || name.Any(c => char.IsControl(c) || "/\\?#%".Contains(c)))
        {
            return new NewUserResult(null, "That is not a valid user name.");
        }

        if (await _ownership.AccountExistsAsync(name, cancellationToken)) return new NewUserResult(null, "That name is taken.");

        var address = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (mode == NewUserPasswordMode.Given && string.IsNullOrEmpty(password))
        {
            return new NewUserResult(null, "A password is required.");
        }

        var chosen = mode == NewUserPasswordMode.Given ? password! : AccountRecoveryService.GeneratePassword();
        var user = new BaGetUser
        {
            UserName = name,
            Email = address,
            EmailConfirmed = address != null,
            Created = _time.UtcNow,
        };

        var result = await _users.CreateAsync(user, chosen);
        if (!result.Succeeded)
        {
            return new NewUserResult(null, string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        return new NewUserResult(user, null, mode == NewUserPasswordMode.Generated ? chosen : null);
    }
}
