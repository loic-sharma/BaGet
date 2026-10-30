using System;

namespace BaGet.Core;

/// <summary>
/// How Docker / OCI repositories behave (<c>Oci:*</c>); read on each request, so a change applies without a restart.
/// </summary>
public class OciOptions
{
    /// <summary>
    /// How long a token from <c>/v2/token</c> (the <c>docker login</c> flow) stays valid. Short on purpose: a token is checked
    /// by its signature and expiry only, so a permission taken away applies when the tokens issued before it have expired.
    /// Default 5 minutes, at least 10 seconds.
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The <c>service</c> the registry names in its <c>WWW-Authenticate</c> challenge and expects back at the token endpoint.
    /// Default <c>BaGet</c>.
    /// </summary>
    public string Service { get; set; } = "BaGet";

    /// <summary>The largest manifest or index accepted, in bytes. Default 4 MiB, as the specification recommends.</summary>
    public int MaxManifestBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// How long an upload session may sit idle before it is discarded (checked whenever a new upload starts). Default 24 hours.
    /// </summary>
    public TimeSpan UploadExpiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a layer that no manifest references stays after it was uploaded before <c>prune</c> may remove it, so a push
    /// in progress (layers first, manifest last) is never pruned. Default 1 hour.
    /// </summary>
    public TimeSpan PruneGrace { get; set; } = TimeSpan.FromHours(1);
}
