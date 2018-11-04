using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Extensions;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGet.Core.Services
{
    public class FilePackageStorageService : IPackageStorageService
    {
        // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
        private const int DefaultCopyBufferSize = 81920;

        private readonly string _storePath;

        public FilePackageStorageService(string storePath)
        {
            _storePath = storePath ?? throw new ArgumentNullException(nameof(storePath));
        }

        public async Task SavePackageStreamAsync(
            PackageArchiveReader package,
            Stream packageStream,
            CancellationToken cancellationToken)
        {
            var identity = await package.GetIdentityAsync(cancellationToken);
            var lowercasedId = identity.Id.ToLowerInvariant();
            var lowercasedNormalizedVersion = identity.Version.ToNormalizedString().ToLowerInvariant();

            var packagePath = PackagePath(lowercasedId, lowercasedNormalizedVersion);
            var nuspecPath = NuspecPath(lowercasedId, lowercasedNormalizedVersion);
            var readmePath = ReadmePath(lowercasedId, lowercasedNormalizedVersion);

            EnsurePathExists(lowercasedId, lowercasedNormalizedVersion);

            // TODO: Uploads should be idempotent. This should fail if and only if the blob
            // already exists but has different content.
            using (var fileStream = File.Open(packagePath, FileMode.CreateNew))
            {
                packageStream.Seek(0, SeekOrigin.Begin);

                await packageStream.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
            }

            using (var nuspec = await package.GetNuspecAsync(cancellationToken))
            using (var fileStream = File.Open(nuspecPath, FileMode.CreateNew))
            {
                await nuspec.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
            }

            if (package.HasReadme())
            {
                using (var readme = package.GetReadme())
                using (var fileStream = File.Open(readmePath, FileMode.CreateNew))
                {
                    await readme.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
                }
            }
        }

        public Task<Stream> GetPackageStreamAsync(string id, NuGetVersion version)
        {
            var packageStream = GetFileStream(id, version, PackagePath);

            return Task.FromResult(packageStream);
        }

        public Task<Stream> GetNuspecStreamAsync(string id, NuGetVersion version)
        {
            var nuspecStream = GetFileStream(id, version, NuspecPath);

            return Task.FromResult(nuspecStream);
        }

        public Task<Stream> GetReadmeStreamAsync(string id, NuGetVersion version)
        {
            var readmeStream = GetFileStream(id, version, ReadmePath);

            return Task.FromResult(readmeStream);
        }

        public Task DeleteAsync(string id, NuGetVersion version)
        {
            var lowercasedId = id.ToLowerInvariant();
            var lowercasedNormalizedVersion = version.ToNormalizedString().ToLowerInvariant();

            var packagePath = PackagePath(lowercasedId, lowercasedNormalizedVersion);
            var nuspecPath = NuspecPath(lowercasedId, lowercasedNormalizedVersion);
            var readmePath = ReadmePath(lowercasedId, lowercasedNormalizedVersion);

            File.Delete(packagePath);
            File.Delete(nuspecPath);
            File.Delete(readmePath);

            return Task.CompletedTask;
        }

        private Stream GetFileStream(string id, NuGetVersion version, Func<string, string, string> pathFunc)
        {
            var versionString = version.ToNormalizedString().ToLowerInvariant();
            var path = pathFunc(id.ToLowerInvariant(), versionString);

            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        private string PackagePath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                _storePath,
                lowercasedId,
                lowercasedNormalizedVersion,
                $"{lowercasedId}.{lowercasedNormalizedVersion}.nupkg");
        }

        private string NuspecPath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                _storePath,
                lowercasedId,
                lowercasedNormalizedVersion,
                $"{lowercasedId}.nuspec");
        }

        private string ReadmePath(string lowercasedId, string lowercasedNormalizedVersion)
        {
            return Path.Combine(
                _storePath,
                lowercasedId,
                lowercasedNormalizedVersion,
                "readme");
        }

        private void EnsurePathExists(string lowercasedId, string lowercasedNormalizedVersion)
        {
            var path = Path.Combine(_storePath, lowercasedId, lowercasedNormalizedVersion);

            Directory.CreateDirectory(path);
        }
    }
}
