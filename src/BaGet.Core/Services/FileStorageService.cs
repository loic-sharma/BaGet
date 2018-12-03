using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core.Services
{
    /// <summary>
    /// Stores content on disk.
    /// </summary>
    public class FileStorageService : IStorageService
    {
        // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
        private const int DefaultCopyBufferSize = 81920;

        private readonly string _storePath;

        public FileStorageService(string storePath)
        {
            _storePath = storePath ?? throw new ArgumentNullException(nameof(storePath));
        }

        public Task<Stream> GetAsync(string path, CancellationToken cancellationToken)
        {
            path = GetFullPath(path);
            var content = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            return Task.FromResult<Stream>(content);
        }

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken)
        {
            var result = new Uri(GetFullPath(path));

            return Task.FromResult(result);
        }

        public async Task<PutResult> PutAsync(
            string path,
            Stream content,
            string contentType,
            CancellationToken cancellationToken)
        {
            // TODO: Uploads should be idempotent. This should fail if and only if the path
            // already exists but has different content.
            path = GetFullPath(path);

            using (var fileStream = File.Open(path, FileMode.CreateNew))
            {
                await content.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
                return PutResult.Success;
            }
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            try
            {
                File.Delete(GetFullPath(path));
            }
            catch (DirectoryNotFoundException)
            {
            }

            return Task.CompletedTask;
        }

        private string GetFullPath(string path)
        {
            // TODO: This should check that the result is in _storePath for security.
            return Path.Combine(_storePath, path);
        }
    }
}
