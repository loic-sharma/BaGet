using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using NuGet.Versioning;

namespace BaGet.Core.Services
{
    public interface IPackageService
    {
        Task AddAsync(Package package);

        Task<Package> FindAsync(string id, NuGetVersion version);
        Task<IReadOnlyList<Package>> FindAsync(string id);

        Task<bool> ExistsAsync(string id, NuGetVersion version);
        Task<bool> UnlistPackageAsync(string id, NuGetVersion version);
        Task<bool> RelistPackageAsync(string id, NuGetVersion version);
    }
}
