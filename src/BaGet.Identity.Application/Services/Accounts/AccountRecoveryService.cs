using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Administrator recovery of an account, for the command line. It needs no email, so it
/// works whether or not SMTP is configured.
/// </summary>
public class AccountRecoveryService
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*-_=+?";

    private readonly UserManager<BaGetUser> _users;

    public AccountRecoveryService(UserManager<BaGetUser> users) => _users = users;

    /// <summary>
    /// Replace the user's password with a generated one and return it, or null if there is
    /// no such user. Two-factor authentication is switched off, its key discarded, and any
    /// lockout lifted, so this also recovers a user who lost their authenticator. The user's
    /// email address is marked confirmed; <paramref name="email"/> sets and confirms one.
    /// </summary>
    public async Task<string?> ResetPasswordAsync(string? userName, CancellationToken cancellationToken, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(userName)) return null;

        #pragma warning disable CS8604
        var user = await _users.FindByNameAsync(userName);
        #pragma warning restore CS8604
        if (user == null) return null;

        // Whoever runs this command is the administrator, so the address is trusted: it is
        // confirmed, or replaced by <paramref name="email"/> and confirmed. Without that, a
        // user with no confirmed address could never sign in once SMTP is switched on.
        if (!string.IsNullOrWhiteSpace(email))
        {
            Check(await _users.SetEmailAsync(user, email.Trim()));
        }

        if (!string.IsNullOrEmpty(user.Email) && !user.EmailConfirmed)
        {
            Check(await _users.ConfirmEmailAsync(user, await _users.GenerateEmailConfirmationTokenAsync(user)));
        }

        var password = GeneratePassword();

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        Check(await _users.ResetPasswordAsync(user, token, password));
        Check(await _users.SetTwoFactorEnabledAsync(user, false));
        Check(await _users.ResetAuthenticatorKeyAsync(user));
        Check(await _users.SetLockoutEndDateAsync(user, null));
        Check(await _users.ResetAccessFailedCountAsync(user));

        return password;
    }

    /// <summary>
    /// A random password with a character of every class Identity's default policy asks for.
    /// </summary>
    internal static string GeneratePassword()
    {
        var chars = new char[20];
        var all = Upper + Lower + Digits + Symbols;

        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < chars.Length; i++) chars[i] = Pick(all);

        RandomNumberGenerator.Shuffle(chars.AsSpan());

        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not update the account: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
