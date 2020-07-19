using System.Collections.Generic;
using System.IO;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGet.Tests
{
    public class PackageData
    {
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

            Default = new MemoryStream();
            builder.Save(Default);
        }

        public static Stream Default { get; }
    }
}
