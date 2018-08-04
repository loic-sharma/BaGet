using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    public interface IMirrorService
    {
        Task MirrorAsync(string id, NuGetVersion version);
    }
}
