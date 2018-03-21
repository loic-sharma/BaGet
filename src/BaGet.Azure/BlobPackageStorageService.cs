using System;
using System.IO;
using System.Threading.Tasks;
using BaGet.Core.Extensions;
using BaGet.Core.Services;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace BaGet.Azure
{
    // See: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Services/CloudBlobCoreFileStorageService.cs
    public class BlobPackageStorageService : IPackageStorageService
    {
        private const int BufferSize = 8192;
        private const string PackageContentType = "binary/octet-stream";
        private const string TextContentType = "text/plain";

        private readonly CloudBlobContainer _container;

        public BlobPackageStorageService(CloudBlobContainer container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public async Task DeleteAsync(PackageIdentity package)
        {
            await GetPackageBlob(package).DeleteIfExistsAsync();
            await GetNuspecBlob(package).DeleteIfExistsAsync();
        }

        public Task<Stream> GetNuspecStreamAsync(PackageIdentity package)
        {
            var blob = GetPackageBlob(package);

            return DownloadBlobAsync(blob);
        }

        public Task<Stream> GetPackageStreamAsync(PackageIdentity package)
        {
            var blob = GetPackageBlob(package);

            return DownloadBlobAsync(blob);
        }

        public async Task SaveAsync(PackageArchiveReader package, Stream packageStream)
        {
            var identity = package.GetIdentity();

            using (var nuspecStream = package.GetNuspec())
            {
                await UploadBlobAsync(GetPackageBlob(identity), packageStream, PackageContentType);
                await UploadBlobAsync(GetNuspecBlob(identity), nuspecStream, TextContentType);

                // TODO: Figure out why this doesn't work.
                // It seems like a container reference can't be used concurrently...
                //return Task.WhenAll(
                //    UploadBlobAsync(GetPackageBlob(identity), packageStream, PackageContentType),
                //    UploadBlobAsync(GetNuspecBlob(identity), nuspecStream, TextContentType));
            }
        }

        private CloudBlockBlob GetPackageBlob(PackageIdentity package)
            => _container.GetBlockBlobReference(package.PackagePath());

        private CloudBlockBlob GetNuspecBlob(PackageIdentity package)
            => _container.GetBlockBlobReference(package.NuspecPath());

        private async Task UploadBlobAsync(CloudBlockBlob blob, Stream content, string contentType)
        {
            await blob.UploadFromStreamAsync(content);

            blob.Properties.ContentType = contentType;

            await blob.SetPropertiesAsync();
        }

        private async Task<Stream> DownloadBlobAsync(CloudBlockBlob blob)
        {
            var stream = new MemoryStream();

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
    }
}
