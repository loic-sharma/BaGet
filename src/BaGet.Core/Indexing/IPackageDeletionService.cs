using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core;

public interface IPackageDeletionService
{
    /// <summary>
    /// Attempt to delete a package.
    /// </summary>
    /// <param name="id">The id of the package to delete.</param>
    /// <param name="version">The version of the package to delete.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>False if the package does not exist.</returns>
    Task<bool> TryDeletePackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Remove the version's rows and files whatever <see cref="BaGetOptions.PackageDeletionBehavior"/> says.
    /// For administrators; the protocol's DELETE keeps following the setting.
    /// </summary>
    /// <returns>False if the database held no such version (its files are removed either way).</returns>
    Task<bool> TryHardDeletePackageAsync(string? id, NuGetVersion version, CancellationToken cancellationToken);
}