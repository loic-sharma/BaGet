using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Extensions;
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
            return await _container
                .GetBlockBlobReference(path)
                .OpenReadAsync(cancellationToken);
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
            var condition = AccessCondition.GenerateIfNotExistsCondition();

            blob.Properties.ContentType = contentType;

            try
            {
                await blob.UploadFromStreamAsync(
                    content,
                    condition,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);

                return PutResult.Success;
            }
            catch (StorageException e) when(IsAlreadyExistsException(e))
            {
                using (var targetStream = await blob.OpenReadAsync(cancellationToken))
                {
                    return content.Matches(targetStream)
                        ? PutResult.AlreadyExists
                        : PutResult.Conflict;
                }
            }
        }

        public async Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            await _container
                .GetBlockBlobReference(path)
                .DeleteIfExistsAsync(cancellationToken);
        }

        public Task<IEnumerable<string>> GetPackagePathsAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Not supported for BlobStorageService");
        }

        private bool IsAlreadyExistsException(StorageException e)
        {
            return e?.RequestInformation?.HttpStatusCode == (int?)HttpStatusCode.Conflict;
        }
    }
}
