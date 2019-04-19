using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Storage;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    public class MirrorPackageStorageService : IPackageStorageService
    {
        public Task DeleteAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<Stream> GetNuspecStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }

        public Task<Stream> GetPackageStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }

        public Task<Stream> GetReadmeStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }

        public Task SavePackageContentAsync(Package package, Stream packageStream, Stream nuspecStream, Stream readmeStream, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
