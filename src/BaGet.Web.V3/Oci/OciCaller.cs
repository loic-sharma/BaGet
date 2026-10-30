using BaGet.Core;

namespace BaGet.Web.V3.Oci;

/// <summary>Who is calling the registry: a registry token, an API key (as the Basic password or a Bearer token), or nobody.</summary>
public sealed class OciCaller
{
    /// <summary>The claims of a valid registry token, or null.</summary>
    public OciTokenClaims? Token { get; init; }

    /// <summary>The API key's principal (an open server gives the superuser), or null. Never set together with <see cref="Token"/>.</summary>
    public BaGetPrincipal? Principal { get; init; }

    /// <summary>A bearer token was sent and was neither a valid registry token nor an API key (expired, tampered with, foreign).</summary>
    public bool TokenRefused { get; init; }

    /// <summary>Whether the caller is anonymous.</summary>
    public bool IsAnonymous => Token == null && Principal == null;

    /// <summary>The name for the log: the token's subject or the key owner's name.</summary>
    public string? Name => Token?.Subject ?? Principal?.UserName;
}
