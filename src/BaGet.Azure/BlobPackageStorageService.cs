using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Extensions;
using BaGet.Core.Services;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using NuGet.Packaging;
using NuGet.Versioning;

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

        public async Task SavePackageStreamAsync(
            PackageArchiveReader package,
            Stream packageStream,
            CancellationToken cancellationToken)
        {
            var identity = await package.GetIdentityAsync(cancellationToken);
            var lowercasedId = identity.Id.ToLowerInvariant();
            var lowercasedNormalizedVersion = identity.Version.ToNormalizedString().ToLowerInvariant();

            var packageBlob = GetPackageBlob(lowercasedId, lowercasedNormalizedVersion);
            var nuspecBlob = GetNuspecBlob(lowercasedId, lowercasedNormalizedVersion);
            var readmeBlob = GetReadmeBlob(lowercasedId, lowercasedNormalizedVersion);

            // Save the package's nupkg
            packageStream.Seek(0, SeekOrigin.Begin);
            await UploadBlobAsync(packageBlob, packageStream, PackageContentType);

            // Save the package's nuspec
            using (var nuspecStream = await package.GetNuspecAsync(cancellationToken))
            {
                await UploadBlobAsync(nuspecBlob, nuspecStream, TextContentType);
            }

            // Save the package's reamde
            using (var readmeStream = package.GetReadme())
            {
                await UploadBlobAsync(readmeBlob, readmeStream, TextContentType);
            }
        }

        public async Task DeleteAsync(string id, NuGetVersion version)
        {
            var lowercasedId = id.ToLowerInvariant();
            var lowercasedNormalizedVersion = version.ToNormalizedString().ToLowerInvariant();

            await GetPackageBlob(lowercasedId, lowercasedNormalizedVersion).DeleteIfExistsAsync();
            await GetNuspecBlob(lowercasedId, lowercasedNormalizedVersion).DeleteIfExistsAsync();
            await GetReadmeBlob(lowercasedId, lowercasedNormalizedVersion).DeleteIfExistsAsync();
        }

        public Task<Stream> GetPackageStreamAsync(string id, NuGetVersion version)
        {
            return GetBlobStreamAsync(id, version, GetPackageBlob);
        }

        public Task<Stream> GetNuspecStreamAsync(string id, NuGetVersion version)
        {
            return GetBlobStreamAsync(id, version, GetNuspecBlob);
        }

        public Task<Stream> GetReadmeStreamAsync(string id, NuGetVersion version)
        {
            return GetBlobStreamAsync(id, version, GetReadmeBlob);
        }

        private async Task<Stream> GetBlobStreamAsync(
            string id,
            NuGetVersion version,
            Func<string, string, CloudBlockBlob> blobFunc)
        {
            var lowercasedId = id.ToLowerInvariant();
            var lowercasedNormalizedVersion = version.ToNormalizedString().ToLowerInvariant();
            var blob = blobFunc(lowercasedId, lowercasedNormalizedVersion);

            return await DownloadBlobAsync(blob);
        }

        // TODO: This should accept a cancellation token.
        private async Task UploadBlobAsync(CloudBlockBlob blob, Stream content, string contentType)
        {
            blob.Properties.ContentType = contentType;

            // TODO: Uploads should be idempotent. This should fail if and only if the blob
            // already exists but has different content.
            await blob.UploadFromStreamAsync(content);
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

        private CloudBlockBlob GetPackageBlob(string lowercasedId, string lowercasedNormalizedVersion)
            => _container.GetBlockBlobReference(PackagePath(lowercasedId, lowercasedNormalizedVersion));

        private CloudBlockBlob GetNuspecBlob(string lowercasedId, string lowercasedNormalizedVersion)
            => _container.GetBlockBlobReference(NuspecPath(lowercasedId, lowercasedNormalizedVersion));

        private CloudBlockBlob GetReadmeBlob(string lowercasedId, string lowercasedNormalizedVersion)
            => _container.GetBlockBlobReference(ReadmePath(lowercasedId, lowercasedNormalizedVersion));

        private string PackagePath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                lowercasedId,
                lowercasedNormalizedVersion,
                $"{lowercasedId}.{lowercasedNormalizedVersion}.nupkg");
        }

        private string NuspecPath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                lowercasedId,
                lowercasedNormalizedVersion,
                $"{lowercasedId}.nuspec");
        }

        private string ReadmePath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                lowercasedId,
                lowercasedNormalizedVersion,
                "readme");
        }
    }
}
