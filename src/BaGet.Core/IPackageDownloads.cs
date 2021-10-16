using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core
{
    public interface IPackageDownloads
    {
        /// <summary>
        /// Increment a package's download count.
        /// </summary>
        /// <param name="packageId">The id of the package to update.</param>
        /// <param name="version">The id of the package to update.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        Task AddAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken);
    }
}
