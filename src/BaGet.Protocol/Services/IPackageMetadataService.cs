using System.Collections.Generic;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public interface IPackageMetadataService
    {
        Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(string packageId, bool includeUnlisted = false);
    }
}
