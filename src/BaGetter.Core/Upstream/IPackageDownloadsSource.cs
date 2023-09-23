using System.Collections.Generic;
using System.Threading.Tasks;

namespace BaGetter.Core
{
    public interface IPackageDownloadsSource
    {
        Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync();
    }
}
