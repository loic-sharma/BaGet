namespace BaGet.Core;

/// <summary>
/// The package format a <see cref="Repository"/> serves. Stored as its name, so a value may be appended
/// but never renumbered or renamed.
/// </summary>
public enum RepositoryFormat
{
    /// <summary>
    /// NuGet (V3 and V2 feeds, symbols).
    /// </summary>
    NuGet = 0,

    /// <summary>
    /// npm (the registry API under <c>npm/</c>). A repository of this format needs the npm <see cref="IFormatModule"/>, which a
    /// later phase registers; until then none can be created.
    /// </summary>
    Npm = 1,

    /// <summary>
    /// Maven 2 (the repository layout under <c>maven/</c>): <c>mvn deploy</c>, <c>mvn dependency:get</c>, Gradle. Local
    /// repositories only for now.
    /// </summary>
    Maven = 2,

    /// <summary>
    /// Docker / OCI images (the OCI Distribution Spec v1.1 under <c>/v2/{repository}/{image}/…</c>): <c>docker login</c>,
    /// <c>push</c> and <c>pull</c>, multi-arch indexes, referrers. Local repositories only for now; needs a store that streams
    /// (<see cref="IStreamingStorageService"/>).
    /// </summary>
    Docker = 3,
}

/// <summary>
/// How a <see cref="Repository"/> gets its content. Stored as its name.
/// </summary>
public enum RepositoryKind
{
    /// <summary>
    /// Holds what is pushed to it.
    /// </summary>
    Local = 0,

    /// <summary>
    /// Caches an upstream repository (<see cref="RemoteRepositorySettings"/>). Read-only for clients: nothing can be
    /// deployed to it, the server itself stores what it fetches.
    /// </summary>
    Remote = 1,

    /// <summary>
    /// Presents several repositories as one.
    /// </summary>
    Virtual = 2,
}
