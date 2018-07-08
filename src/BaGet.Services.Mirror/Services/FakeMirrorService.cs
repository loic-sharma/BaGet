using NuGet.Versioning;
using System.Threading.Tasks;

namespace BaGet.Services.Mirror
{
    public class FakeMirrorService : IMirrorService
    {
        public Task MirrorAsync(string id, NuGetVersion version) => Task.CompletedTask;
    }
}
