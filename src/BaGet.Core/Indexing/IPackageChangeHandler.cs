using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// Told about every change to a package version after it has been committed. Register
/// any number of these; <see cref="PackageChangeNotifier"/> calls each in turn.
/// </summary>
public interface IPackageChangeHandler
{
    /// <summary>
    /// The package version was added, relisted or unlisted, or its metadata changed.
    /// </summary>
    Task OnPackageChangedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// The package version was permanently deleted.
    /// </summary>
    Task OnPackageDeletedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken);
}