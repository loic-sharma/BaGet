namespace BaGet.Core;

/// <summary>
/// Asynchronous validation of pushed packages.
/// </summary>
public class ValidationOptions
{
    /// <summary>
    /// Off by default. With it off a push is indexed at once, exactly as before.
    /// With it on, a push answers 202 Accepted and the package appears once every
    /// validator has passed; a package that fails is quarantined, never served.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Reject packages without a valid author or repository signature.
    /// </summary>
    public bool RequireSignedPackages { get; set; }

    /// <summary>
    /// A PFX file whose certificate repository-signs every package that passes
    /// validation. Unused unless set; the RepositorySignatures resource is only
    /// advertised when it is.
    /// </summary>
    public string? RepositorySigningCertificatePath { get; set; }

    [SensitiveSetting]
    public string? RepositorySigningCertificatePassword { get; set; }

    /// <summary>
    /// This server's public service index URL, recorded in every repository signature;
    /// a client checks it against the source it restored from. Required for signing,
    /// since signing runs in the background, outside any request to read it from, and
    /// it must be https: NuGet rejects a repository signature for any other scheme.
    /// </summary>
    public string? RepositorySigningServiceIndexUrl { get; set; }
}