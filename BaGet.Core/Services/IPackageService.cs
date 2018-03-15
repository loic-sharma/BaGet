using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using NuGet.Versioning;

namespace BaGet.Core.Services
{
    public interface IPackageService
    {
        Task<bool> ExistsAsync(string id, NuGetVersion version);

        Task<IReadOnlyList<Package>> FindAsync(string id);

        Task<Package> FindAsync(string id, NuGetVersion version);
    }
}
