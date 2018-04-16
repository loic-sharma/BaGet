using System;
using System.IO;
using System.Threading.Tasks;
using BaGet.Core.Extensions;
using BaGet.Core.Services;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace BaGet.Azure.Configuration
{
    // See: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Services/CloudBlobCoreFileStorageService.cs
    public class BlobPackageStorageService : IPackageStorageService
    {
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
            await GetReadmeBlob(package).DeleteIfExistsAsync();
        }

        public Task<Stream> GetPackageStreamAsync(PackageIdentity package)
        {
            var blob = GetPackageBlob(package);

            return DownloadBlobAsync(blob);
        }
        public Task<Stream> GetNuspecStreamAsync(PackageIdentity package)
        {
            var blob = GetNuspecBlob(package);

            return DownloadBlobAsync(blob);
        }

        public async Task<Stream> GetReadmeStreamAsync(PackageIdentity package)
        {
            var blob = GetReadmeBlob(package);

            if (!await blob.ExistsAsync())
            {
                return Stream.Null;
            }

            return await DownloadBlobAsync(blob);
        }

        public async Task SavePackageStreamAsync(PackageArchiveReader package, Stream packageStream)
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

            using (var readmeStream = package.GetReadme())
            {
                if (readmeStream != Stream.Null)
                {
                    await UploadBlobAsync(GetReadmeBlob(identity), readmeStream, TextContentType);
                }
            }
        }

        private CloudBlockBlob GetPackageBlob(PackageIdentity package)
            => _container.GetBlockBlobReference(package.PackagePath());

        private CloudBlockBlob GetNuspecBlob(PackageIdentity package)
            => _container.GetBlockBlobReference(package.NuspecPath());

        private CloudBlockBlob GetReadmeBlob(PackageIdentity package)
            => _container.GetBlockBlobReference(package.ReadmePath());


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
