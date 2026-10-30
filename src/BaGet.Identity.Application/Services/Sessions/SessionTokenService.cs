using System;
using System.Security.Cryptography;
using System.Text.Json;
using BaGet.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace BaGet.Identity.Application.Services.Sessions;

/// <summary>
/// Issues and reads the tokens of an interactive session (<c>/api/ui/session</c>): stateless, ASP.NET Core data protection over a JSON payload
/// (user id, security stamp, authentication time, expiry), one purpose per <see cref="SessionTokenKind"/>, a <see cref="Prefix"/> that tells them from
/// API keys. Nothing is stored; <see cref="SessionAuthenticator"/> re-validates the account and the stamp on every use, so changing the password or
/// signing out everywhere ends every token. Only <c>/api/ui</c> accepts them (a protocol route reads them as an invalid key).
/// </summary>
public sealed class SessionTokenService
{
    /// <summary>The start of every session token, so it is never mistaken for an API key.</summary>
    public const string Prefix = "bgs.";

    /// <summary>How long the password step of a two-factor sign-in stays valid.</summary>
    public static readonly TimeSpan TwoFactorLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long the exchange token of an external sign-in stays valid.</summary>
    public static readonly TimeSpan ExchangeLifetime = TimeSpan.FromSeconds(60);

    private const string Purpose = "BaGet.Session.Token.v1";
    private const int MaxLength = 4 * 1024;

    private readonly IDataProtectionProvider? _protection;
    private readonly SystemTime _time;
    private readonly IOptionsSnapshot<AccountsOptions> _options;

    /// <summary>Creates the service.</summary>
    public SessionTokenService(SystemTime time, IOptionsSnapshot<AccountsOptions> options, IDataProtectionProvider? protection = null)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _protection = protection;
    }

    /// <summary>Whether data protection exists, without which no token can be issued.</summary>
    public bool IsAvailable => _protection != null;

    /// <summary>Whether <paramref name="credential"/> looks like a session token (it may still be invalid).</summary>
    public static bool IsSessionToken(string? credential)
        => credential != null && credential.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The lifetime of a token of <paramref name="kind"/>.</summary>
    public TimeSpan LifetimeOf(SessionTokenKind kind)
    {
        var accounts = _options.Value;

        return kind switch
        {
            SessionTokenKind.Access => AtLeast(accounts.SessionTokenLifetime, TimeSpan.FromSeconds(10)),
            SessionTokenKind.Refresh => AtLeast(accounts.SessionRefreshLifetime, TimeSpan.FromMinutes(1)),
            SessionTokenKind.Exchange => ExchangeLifetime,
            _ => TwoFactorLifetime,
        };
    }

    private static TimeSpan AtLeast(TimeSpan value, TimeSpan floor) => value < floor ? floor : value;

    /// <summary>Issues a token for the account; <paramref name="authenticatedAt"/> is the time of the sign-in it belongs to.</summary>
    public IssuedSessionToken Issue(SessionTokenKind kind, string userId, string securityStamp, DateTime authenticatedAt)
    {
        if (_protection == null) throw new InvalidOperationException("Session tokens need ASP.NET Core data protection, which this host does not provide.");

        var expires = DateTime.SpecifyKind(_time.UtcNow, DateTimeKind.Utc) + LifetimeOf(kind);

        // However often it is refreshed, a session ends SessionMaxLifetime after the sign-in.
        if (kind is SessionTokenKind.Access or SessionTokenKind.Refresh)
        {
            var end = DateTime.SpecifyKind(authenticatedAt, DateTimeKind.Utc) + AtLeast(_options.Value.SessionMaxLifetime, TimeSpan.FromMinutes(1));
            if (end < expires) expires = end;
        }
        var payload = new SessionTokenPayload
        {
            UserId = userId,
            SecurityStamp = securityStamp,
            AuthenticatedAt = Seconds(authenticatedAt),
            Expires = Seconds(expires),
        };

        var bytes = Protector(kind).Protect(JsonSerializer.SerializeToUtf8Bytes(payload));

        return new IssuedSessionToken(Prefix + Base64Url(bytes), DateTimeOffset.FromUnixTimeSeconds(payload.Expires).UtcDateTime);
    }

    /// <summary>The claims of a valid, unexpired token of <paramref name="kind"/>, or null (tampered with, expired, another kind, another key ring).</summary>
    public SessionTokenClaims? Validate(SessionTokenKind kind, string? token)
    {
        if (_protection == null || !IsSessionToken(token) || token!.Length > MaxLength) return null;

        try
        {
            var payload = JsonSerializer.Deserialize<SessionTokenPayload>(Protector(kind).Unprotect(FromBase64Url(token.Substring(Prefix.Length))));
            if (payload == null || payload.Version != 1 || string.IsNullOrEmpty(payload.UserId)) return null;

            var expires = DateTimeOffset.FromUnixTimeSeconds(payload.Expires).UtcDateTime;
            if (expires <= _time.UtcNow) return null;

            return new SessionTokenClaims(
                payload.UserId,
                payload.SecurityStamp ?? string.Empty,
                DateTimeOffset.FromUnixTimeSeconds(payload.AuthenticatedAt).UtcDateTime,
                expires);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private IDataProtector Protector(SessionTokenKind kind) => _protection!.CreateProtector(Purpose, kind.ToString());

    private static long Seconds(DateTime value)
        => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

        return Convert.FromBase64String(padded);
    }
}
