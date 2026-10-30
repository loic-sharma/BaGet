namespace BaGet.Core;

/// <summary>
/// Which reverse proxies BaGet believes when they send <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> (<c>Proxy:*</c>).
/// By default only loopback proxies are trusted (the framework default), so a client that reaches BaGet directly cannot
/// spoof its address or the scheme. Behind a proxy on another address, list it; <see cref="TrustAnyProxy"/> restores the
/// older "believe every sender" behaviour and must be an explicit choice.
/// </summary>
[RestartRequired]
public class ProxyOptions
{
    /// <summary>Addresses of trusted proxies, separated by semicolons (<c>10.0.0.5;fd00::1</c>), added to the loopback ones.</summary>
    public string? TrustedProxies { get; set; }

    /// <summary>Trusted proxy networks in CIDR form, separated by semicolons (<c>10.0.0.0/8;172.16.0.0/12</c>).</summary>
    public string? TrustedNetworks { get; set; }

    /// <summary>
    /// Believe the forwarded headers of every sender. Only safe when BaGet is reachable through the proxy alone (no direct
    /// route from clients): otherwise any client can forge its address (defeating the rate limits) and the scheme.
    /// </summary>
    public bool TrustAnyProxy { get; set; }
}
