using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core
{
    public interface IPackageContentsContext
    {
        DbSet<PackageContents> PackageContents { get; set; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    }
}
