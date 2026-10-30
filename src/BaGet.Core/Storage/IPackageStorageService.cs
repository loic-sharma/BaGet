using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// Stores packages' content. Packages' state are stored by the
/// <see cref="IPackageDatabase"/>.
/// </summary>
public interface IPackageStorageService
{
    /// <summary>
    /// Persist a package's content to storage. This operation MUST NOT store the content if a
    /// package with the same id/version but different content has already been stored.
    /// </summary>
    /// <param name="package">The package's metadata.</param>
    /// <param name="packageStream">The package's nupkg stream.</param>
    /// <param name="nuspecStream">The package's nuspec stream.</param>
    /// <param name="readmeStream">The package's readme stream, or null if none.</param>
    /// <param name="iconStream">The package's icon stream, or null if none.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The result of the attempted save operation.</returns>
    Task<PackageSaveResult> SavePackageContentAsync(
        Package package,
        Stream? packageStream,
        Stream nuspecStream,
        Stream? readmeStream,
        Stream? iconStream,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve a package's nupkg stream.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The package's nupkg stream, or <see langword="null"/> if it is missing from storage.</returns>
    Task<Stream?> GetPackageStreamAsync(string? id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve a package's nuspec stream.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The package's nuspec stream, or <see langword="null"/> if it is missing from storage.</returns>
    Task<Stream?> GetNuspecStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve a package's readme stream.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The package's readme stream, or <see langword="null"/> if it is missing from storage.</returns>
    Task<Stream?> GetReadmeStreamAsync(string? id, NuGetVersion version, CancellationToken cancellationToken);

    Task<Stream?> GetIconStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Remove a package's content from storage. This operation SHOULD succeed
    /// even if the package does not exist.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task DeleteAsync(string? id, NuGetVersion version, CancellationToken cancellationToken);
}

/// <summary>
/// The result of a <see cref="IPackageStorageService.SavePackageContentAsync(Package, Stream, Stream, Stream, Stream, CancellationToken)"/> operation.
/// </summary>
public enum PackageSaveResult
{
    /// <summary>
    /// Content for this package's id/version has already been stored and does not
    /// match the content that was submitted. Nothing further was stored.
    /// </summary>
    Conflict,

    /// <summary>
    /// The package's content was stored, or the exact same content had already been stored.
    /// </summary>
    Success,
}