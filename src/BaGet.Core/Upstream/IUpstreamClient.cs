using NuGet.Versioning;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// A client to interact with an upstream package source.
/// </summary>
public interface IUpstreamClient
{
    /// <summary>
    /// Try to get all versions of a package from the upstream package source. Returns empty
    /// if the package could not be found.
    /// </summary>
    /// <param name="id">The package ID to lookup</param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// All versions of a package, including unlisted packages.
    /// Returns an empty list if the package could not be found.
    /// </returns>
    Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Try to get the metadata for all versions of a package from the upstream package source. Returns empty
    /// if the package could not be found.
    /// </summary>
    /// <param name="id">The package ID to lookup</param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// The metadata for all versions of a package, including unlisted versions.
    /// Returns an empty list if the package could not be found.
    /// </returns>
    Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Download a package from the upstream package source. Returns null if the package does not exist.
    /// </summary>
    /// <param name="id">The package ID to download.</param>
    /// <param name="version">The package version to download.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// The package stream or null if the package cannot be found.
    /// The stream is guaranteed to be seekable if not not null.
    /// </returns>
    Task<Stream?> DownloadPackageOrNullAsync(string? id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <see cref="DownloadPackageOrNullAsync"/> would find the version, without downloading it: the same rules
    /// (offline mode, a remembered "not found") applied to a version list. What the resolution preview of a virtual
    /// repository asks. The default asks <see cref="ListPackageVersionsAsync"/>.
    /// </summary>
    async Task<UpstreamProbe> ProbeAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        => (await ListPackageVersionsAsync(id, cancellationToken).ConfigureAwait(false)).Contains(version)
            ? UpstreamProbe.Available
            : UpstreamProbe.Absent;
}

/// <summary>What <see cref="IUpstreamClient.ProbeAsync"/> found.</summary>
public enum UpstreamProbe
{
    /// <summary>The upstream does not have the version (or there is no upstream).</summary>
    Absent,

    /// <summary>The upstream has the version: a download would fetch it.</summary>
    Available,

    /// <summary>Offline mode: the upstream is never asked, so a download finds nothing there.</summary>
    Offline,

    /// <summary>The upstream could not be reached.</summary>
    Unavailable,
}