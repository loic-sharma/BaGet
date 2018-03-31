using System;
using System.IO;
using System.Threading.Tasks;
using BaGet.Core.Extensions;
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
            var packagePath = Path.Combine(_storePath, identity.PackagePath());
            var nuspecPath = Path.Combine(_storePath, identity.NuspecPath());

            EnsurePathExists(identity);

            // TODO: Catch IOException and test if File.Exists. If false, rethrow exception.
            using (var fileStream = File.Open(packagePath, FileMode.CreateNew))
            {
                packageStream.Seek(0, SeekOrigin.Begin);

                await packageStream.CopyToAsync(fileStream);
            }

            using (var nuspec = package.GetNuspec())
            using (var fileStream = File.Open(nuspecPath, FileMode.CreateNew))
            {
                await nuspec.CopyToAsync(fileStream);
            }
        }

        public Task<Stream> GetPackageStreamAsync(PackageIdentity package) => Task.FromResult(GetPackageStream(package));
        public Task<Stream> GetNuspecStreamAsync(PackageIdentity package) => Task.FromResult(GetNuspecStream(package));

        private Stream GetPackageStream(PackageIdentity package)
        {
            var path = Path.Combine(_storePath, package.PackagePath());

            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        private Stream GetNuspecStream(PackageIdentity package)
        {
            var path = Path.Combine(_storePath, package.NuspecPath());

            return File.Open(path, FileMode.Open);
        }

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
    }
}
