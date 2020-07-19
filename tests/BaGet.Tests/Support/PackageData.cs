using System.Collections.Generic;
using System.IO;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGet.Tests
{
    public class PackageData
    {
        private static readonly byte[] DefaultBytes;

        static PackageData()
        {
            var builder = new PackageBuilder();

            builder.Id = "DefaultPackage";
            builder.Authors.Add("Default package author");
            builder.Description = "Default package description";
            builder.Version = NuGetVersion.Parse("1.2.3");

            builder.Files.Add(new PhysicalPackageFile(new MemoryStream())
            {
                TargetPath = "lib/netstandard2.0/_._"
            });

            using var defaultStream = new MemoryStream();
            builder.Save(defaultStream);

            DefaultBytes = defaultStream.ToArray();
        }

        // Create a new stream each time so that tests can run concurrently.
        public static Stream Default => new MemoryStream(DefaultBytes, writable: false);
    }
}
