using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Services.Mirror
{
    public interface IMirrorService
    {
        Task MirrorAsync(string id, NuGetVersion version);
    }
}
