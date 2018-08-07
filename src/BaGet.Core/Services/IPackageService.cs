using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using NuGet.Versioning;

namespace BaGet.Core.Services
{
    public enum PackageAddResult
    {
        PackageAlreadyExists,
        Success
    }

    public interface IPackageService
    {
        Task<PackageAddResult> AddAsync(Package package);

        Task<Package> FindAsync(string id, NuGetVersion version, bool includeUnlisted = false);
        Task<IReadOnlyList<Package>> FindAsync(string id, bool includeUnlisted = false);

        Task<bool> ExistsAsync(string id, NuGetVersion version);
        Task<bool> UnlistPackageAsync(string id, NuGetVersion version);
        Task<bool> RelistPackageAsync(string id, NuGetVersion version);
        Task<bool> AddDownloadAsync(string id, NuGetVersion version);
    }
}
