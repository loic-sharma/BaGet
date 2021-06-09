using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BaGet.Azure
{
    // See: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Services/CloudBlobCoreFileStorageService.cs
    public class BlobStorageService : IStorageService
    {
        private readonly CloudBlobContainer _container;

        public BlobStorageService(CloudBlobContainer container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public async Task<Stream> GetAsync(Blob blob, CancellationToken cancellationToken)
        {
            return await _container
                .GetBlockBlobReference(blob.Path)
                .OpenReadAsync(cancellationToken);
        }

        public Task<Uri> GetDownloadUriAsync(Blob blob, CancellationToken cancellationToken)
        {
            // TODO: Make expiry time configurable.
            var blobRef = _container.GetBlockBlobReference(blob.Path);
            var accessPolicy = new SharedAccessBlobPolicy
            {
                SharedAccessExpiryTime = DateTimeOffset.Now.Add(TimeSpan.FromMinutes(10)),
                Permissions = SharedAccessBlobPermissions.Read
            };

            var signature = blobRef.GetSharedAccessSignature(accessPolicy);
            var result = new Uri(blobRef.Uri, signature);

            return Task.FromResult(result);
        }

        public async Task<StoragePutResult> PutAsync(
            Blob blob,
            Stream content,
            string contentType,
            CancellationToken cancellationToken)
        {
            var blobRef = _container.GetBlockBlobReference(blob.Path);
            var condition = AccessCondition.GenerateIfNotExistsCondition();

            blobRef.Properties.ContentType = contentType;

            try
            {
                await blobRef.UploadFromStreamAsync(
                    content,
                    condition,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);

                return StoragePutResult.Success;
            }
            catch (StorageException e) when (e.IsAlreadyExistsException())
            {
                using (var targetStream = await blobRef.OpenReadAsync(cancellationToken))
                {
                    content.Position = 0;
                    return content.Matches(targetStream)
                        ? StoragePutResult.AlreadyExists
                        : StoragePutResult.Conflict;
                }
            }
        }

        public async Task DeleteAsync(Blob blob, CancellationToken cancellationToken)
        {
            await _container
                .GetBlockBlobReference(blob.Path)
                .DeleteIfExistsAsync(cancellationToken);
        }
    }
}
