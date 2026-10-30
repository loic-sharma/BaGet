using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Raised when a connection is refused because <c>Remote:BlockPrivateAddresses</c> forbids the address.
/// </summary>
internal sealed class PrivateAddressException : Exception
{
    public PrivateAddressException() : base(RemoteAddressPolicy.Message)
    {
    }
}

/// <summary>
/// Decides which upstream addresses <see cref="RemoteOptions.BlockPrivateAddresses"/> refuses.
/// </summary>
public static class RemoteAddressPolicy
{
    /// <summary>The reason shown when an address is refused.</summary>
    public const string Message = "The upstream is a loopback, private, link-local or otherwise non-public address, which Remote:BlockPrivateAddresses refuses.";

    /// <summary>Whether <paramref name="address"/> is not a public unicast address.</summary>
    public static bool IsBlocked(IPAddress address)
    {
        if (address == null) throw new ArgumentNullException(nameof(address));

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 0                                                 // 0.0.0.0/8
                || bytes[0] == 10                                                // 10.0.0.0/8
                || (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)                  // 100.64.0.0/10 carrier-grade NAT
                || (bytes[0] == 169 && bytes[1] == 254)                          // 169.254.0.0/16 link-local, cloud metadata
                || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16)                  // 172.16.0.0/12
                || (bytes[0] == 192 && bytes[1] == 168)                          // 192.168.0.0/16
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)           // 192.0.0.0/24
                || bytes[0] >= 224;                                              // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv6 forms that carry an IPv4 address are judged by the address they carry: IPv4-compatible ::a.b.c.d,
            // NAT64 64:ff9b::/96 and 6to4 2002::/16. Local-use NAT64 64:ff9b:1::/48 is refused whole.
            var embedded = EmbeddedIPv4(bytes);
            if (embedded != null) return IsBlocked(embedded);
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b && bytes[4] == 0x00 && bytes[5] == 0x01) return true;

            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (bytes[0] & 0xFE) == 0xFC;                                    // fc00::/7 unique-local
        }

        return true;
    }

    private static IPAddress? EmbeddedIPv4(byte[] v6)
    {
        var tail = new byte[4];
        if (v6.Take(12).All(b => b == 0) && !(v6[12] == 0 && v6[13] == 0 && v6[14] == 0 && v6[15] <= 1))
        {
            Array.Copy(v6, 12, tail, 0, 4);                                   // ::a.b.c.d (:: and ::1 are judged as IPv6)
        }
        else if (v6[0] == 0x00 && v6[1] == 0x64 && v6[2] == 0xff && v6[3] == 0x9b && v6.Skip(4).Take(8).All(b => b == 0))
        {
            Array.Copy(v6, 12, tail, 0, 4);                                   // 64:ff9b::/96
        }
        else if (v6[0] == 0x20 && v6[1] == 0x02)
        {
            Array.Copy(v6, 2, tail, 0, 4);                                    // 2002::/16, the IPv4 address follows the prefix
        }
        else
        {
            return null;
        }

        return new IPAddress(tail);
    }

    /// <summary>
    /// A problem with the host named in <paramref name="url"/> that needs no DNS lookup (<c>localhost</c> or a literal
    /// address), or null.
    /// </summary>
    public static string? CheckLiteral(Uri? url)
    {
        if (url == null) return null;

        var host = url.IdnHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return Message;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsBlocked(address) ? Message : null;
    }

    /// <summary>
    /// A problem with the host of <paramref name="url"/> after resolving it (any resolved address refused refuses the
    /// host), or null. A name that does not resolve is not a policy problem (the connection will fail on its own).
    /// </summary>
    public static async Task<string?> CheckResolvedAsync(Uri? url, CancellationToken cancellationToken)
    {
        var literal = CheckLiteral(url);
        if (literal != null || url == null) return literal;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(url.IdnHost, cancellationToken).ConfigureAwait(false);

            return addresses.Any(IsBlocked) ? Message : null;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
