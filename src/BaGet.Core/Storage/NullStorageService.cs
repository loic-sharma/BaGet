using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core.Services
{
    /// <summary>
    /// A minimal storage implementation, used for advanced scenarios.
    /// </summary>
    public class NullStorageService : IStorageService
    {
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<PutResult> PutAsync(
            string path,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PutResult.Success);
        }
    }
}
