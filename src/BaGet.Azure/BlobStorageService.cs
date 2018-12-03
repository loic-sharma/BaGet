using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BaGet.Azure.Configuration
{
    // See: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Services/CloudBlobCoreFileStorageService.cs
    public class BlobStorageService : IStorageService
    {
        private readonly CloudBlobContainer _container;

        public BlobStorageService(CloudBlobContainer container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public async Task<Stream> GetAsync(string path, CancellationToken cancellationToken)
        {
            var stream = new MemoryStream();
            var blob = _container.GetBlockBlobReference(path);

            try
            {
                await blob.DownloadToStreamAsync(stream);
            }
            catch (StorageException)
            {
                stream.Dispose();

                // TODO
                throw;
            }

            stream.Position = 0;

            return stream;
        }

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken)
        {
            // TODO: Make expiry time configurable.
            var blob = _container.GetBlockBlobReference(path);
            var accessPolicy = new SharedAccessBlobPolicy
            {
                SharedAccessExpiryTime = DateTimeOffset.Now.Add(TimeSpan.FromMinutes(10)),
                Permissions = SharedAccessBlobPermissions.Read
            };

            var signature = blob.GetSharedAccessSignature(accessPolicy);
            var result = new Uri(blob.Uri, signature);

            return Task.FromResult(result);
        }

        public async Task<PutResult> PutAsync(
            string path,
            Stream content,
            string contentType,
            CancellationToken cancellationToken)
        {
            var blob = _container.GetBlockBlobReference(path);

            blob.Properties.ContentType = contentType;

            // TODO: Uploads should be idempotent. This should fail if and only if the blob
            // already exists but has different content.
            // TODO: Pass the cancellation token down.
            await blob.UploadFromStreamAsync(content);
            return PutResult.Success;
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            // TODO: Pass cancellation token down.
            await _container.GetBlockBlobReference(path).DeleteIfExistsAsync();
        }
    }
}
