using System;
using System.IO;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace BaGet.Core.Services
{
    public class FilePackageStorageService : IPackageStorageService
    {
        private readonly string _storePath;

        public FilePackageStorageService(string storePath)
        {
            _storePath = storePath ?? throw new ArgumentNullException(nameof(storePath));
        }

        public async Task SaveAsync(PackageArchiveReader package, Stream packageStream)
        {
            var identity = package.GetIdentity();

            EnsurePathExists(identity);

            using (var fileStream = File.Open(PackagePath(identity), FileMode.CreateNew))
            {
                packageStream.Seek(0, SeekOrigin.Begin);

                await packageStream.CopyToAsync(fileStream);
            }

            using (var nuspec = package.GetNuspec())
            using (var fileStream = File.Open(NuspecPath(identity), FileMode.CreateNew))
            {
                await nuspec.CopyToAsync(fileStream);
            }
        }

        public Task<Stream> GetPackageStreamAsync(PackageIdentity package) => Task.FromResult(GetPackageStream(package));
        public Task<Stream> GetNuspecStreamAsync(PackageIdentity package) => Task.FromResult(GetNuspecStream(package));

        private Stream GetPackageStream(PackageIdentity package) => File.Open(PackagePath(package), FileMode.Open);
        private Stream GetNuspecStream(PackageIdentity package) => File.Open(NuspecPath(package), FileMode.Open);

        public Task DeleteAsync(PackageIdentity package)
        {
            throw new NotImplementedException();
        }

        private void EnsurePathExists(PackageIdentity package)
        {
            var id = package.Id;
            var version = package.Version.ToNormalizedString();
            var path = Path.Combine(_storePath, id, version);

            Directory.CreateDirectory(path);
        }

        private string PackagePath(PackageIdentity package)
        {
            var id = package.Id.ToLowerInvariant();
            var version = package.Version.ToNormalizedString().ToLowerInvariant();

            return Path.Combine(_storePath, id, version, $"{id}.{version}.nupkg");
        }

        private string NuspecPath(PackageIdentity package)
        {
            var id = package.Id.ToLowerInvariant();
            var version = package.Version.ToNormalizedString().ToLowerInvariant();

            return Path.Combine(_storePath, id, version, $"{id}.nuspec");
        }
    }
}
