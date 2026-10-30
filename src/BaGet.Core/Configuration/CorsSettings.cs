namespace BaGet.Core;

/// <summary>
/// Which web origins may call BaGet's APIs from a browser (<c>Cors:*</c>), read by the host at startup.
/// </summary>
[RestartRequired]
public class CorsSettings
{
    /// <summary>
    /// The origins (<c>https://app.example.com</c>) allowed to call BaGet from a browser, separated by semicolons. Empty (the default) allows
    /// <b>any</b> origin, without credentials: the APIs authenticate with headers a page must set itself, so no ambient credential can be ridden.
    /// With a list, only those origins are allowed and they may send credentials; the same list decides where the external sign-in of a
    /// single-page application may return tokens to.
    /// </summary>
    public string? AllowedOrigins { get; set; }
}
