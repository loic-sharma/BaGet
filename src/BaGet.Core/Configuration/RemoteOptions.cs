namespace BaGet.Core;

/// <summary>
/// How remote repositories may reach their upstreams (<c>Remote:*</c>).
/// </summary>
public class RemoteOptions
{
    /// <summary>
    /// Whether an upstream may not be a loopback, private (RFC 1918, unique-local), link-local (including the cloud
    /// metadata address 169.254.169.254), carrier-grade NAT or other non-public address. Off by default because an
    /// internal feed is the normal use of a remote repository; turn it on when the administrators who may create
    /// remote repositories are less trusted than the host, since otherwise an upstream URL (and Test connection)
    /// reaches whatever this server can reach. Checked when a repository is created or saved, when Test connection runs,
    /// and on every connection a V3 upstream client opens (so also after a DNS change or a redirect). A V2 upstream
    /// is contacted by NuGet's own HTTP stack and only the address in the URL is checked.
    /// </summary>
    public bool BlockPrivateAddresses { get; set; }
}
