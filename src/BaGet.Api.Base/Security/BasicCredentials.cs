using System;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace BaGet.Frontend.Security;

/// <summary>
/// Credentials sent with <c>Authorization: Basic</c>, which is what NuGet clients (dotnet, nuget.exe, Visual Studio,
/// PowerShellGet) send to a feed that answered 401 with <c>WWW-Authenticate: Basic</c>.
/// </summary>
public static class BasicCredentials
{
    /// <summary>The challenge a protected feed answers anonymous callers with.</summary>
    public const string Challenge = "Basic realm=\"BaGet\", charset=\"UTF-8\"";

    /// <summary>The challenge an npm route adds: npm clients send <c>Authorization: Bearer {_authToken}</c>.</summary>
    public const string BearerChallenge = "Bearer realm=\"BaGet\"";

    /// <summary>
    /// The token of an <c>Authorization: Bearer</c> header (what npm sends for <c>_authToken</c>), or null. The token is an
    /// API key, like the password of Basic credentials.
    /// </summary>
    public static string? ReadBearer(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return null;

        var token = header.Substring(scheme.Length).Trim();

        return token.Length == 0 ? null : token;
    }

    /// <summary>
    /// The user name and password of the request's Basic credentials, or null when it carries none (or malformed ones).
    /// The password is an API key; the user name is the key owner's, or anything for the global key.
    /// </summary>
    public static (string UserName, string Password)? Read(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string scheme = "Basic ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring(scheme.Length).Trim()));
            var colon = decoded.IndexOf(':');
            if (colon < 0) return null;

            return (decoded.Substring(0, colon), decoded.Substring(colon + 1));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
