using System.Collections.Generic;
using System.Security.Claims;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Reads a claim of an external principal by the name a provider uses in its tokens. The authentication handler maps some well-known names to the
/// .NET claim types (<c>email</c> to <see cref="ClaimTypes.Email"/>), so both spellings are tried.
/// </summary>
public static class ExternalClaims
{
    // What the OpenID Connect handler does to incoming claim types (MapInboundClaims): raw name -> .NET type (role and roles -> ClaimTypes.Role, group -> the Group URI, ...).
    private static readonly IDictionary<string, string> Inbound = Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.DefaultInboundClaimTypeMap;

    /// <summary>The claims named <paramref name="name"/> (exact name first, else the .NET type the handler maps it to through <c>JsonWebTokenHandler.DefaultInboundClaimTypeMap</c>).</summary>
    public static IReadOnlyList<string> Values(ClaimsPrincipal principal, string? name)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(name)) return result;

        foreach (var type in Types(name.Trim()))
        {
            foreach (var claim in principal.FindAll(type))
            {
                if (!string.IsNullOrEmpty(claim.Value)) result.Add(claim.Value);
            }

            if (result.Count > 0) break;
        }

        return result;
    }

    /// <summary>The first value of the claim, or null.</summary>
    public static string? First(ClaimsPrincipal principal, string? name)
    {
        var values = Values(principal, name);

        return values.Count > 0 ? values[0] : null;
    }

    // The name as written, then the type the handler maps it to, then every other raw name that maps to the same type as a claim written as such.
    private static IEnumerable<string> Types(string name)
    {
        yield return name;
        if (Inbound.TryGetValue(name, out var mapped)) yield return mapped;
    }
}
