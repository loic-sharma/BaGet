using System.IO;
using NuGet.Packaging.Core;

namespace BaGet.Core.Extensions
{
    public static class PackageIdentityExtensions
    {
        public static string PackagePath(this PackageIdentity package)
        {
            var id = package.Id.ToLowerInvariant();
            var version = package.Version.ToNormalizedString().ToLowerInvariant();

            return Path.Combine(id, version, $"{id}.{version}.nupkg");
        }

        public static string NuspecPath(this PackageIdentity package)
        {
            var id = package.Id.ToLowerInvariant();
            var version = package.Version.ToNormalizedString().ToLowerInvariant();

            return Path.Combine(id, version, $"{id}.nuspec");
        }

        public static string ReadmePath(this PackageIdentity package)
        {
            var id = package.Id.ToLowerInvariant();
            var version = package.Version.ToNormalizedString().ToLowerInvariant();

            return Path.Combine(id, version, "readme");
        }
    }
}
