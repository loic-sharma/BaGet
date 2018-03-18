using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core.Entities
{
    public interface IContext
    {
        DbSet<Package> Packages { get; set; }

        Task<int> SaveChangesAsync();
    }
}
