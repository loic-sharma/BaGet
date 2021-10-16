using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core
{
    public class PackageDownloads : IPackageDownloads
    {
        private readonly IPackageDatabase _db;

        public PackageDownloads(IPackageDatabase db)
        {
            _db = db;
        }

        public async Task AddAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
        {
            await _db.AddDownloadAsync(packageId, version, cancellationToken);
        }
    }
}
