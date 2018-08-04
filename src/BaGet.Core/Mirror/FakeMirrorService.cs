using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    public class FakeMirrorService : IMirrorService
    {
        public Task MirrorAsync(string id, NuGetVersion version) => Task.CompletedTask;
    }
}
