using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Packaging;

namespace BaGet.Core.Extensions
{
    public static class PackageArchiveReaderExtensions
    {
        private static readonly string[] OrderedReadmeFileNames = new[]
        {
            "readme.md",
            "readme.txt",
        };

        private static readonly HashSet<string> ReadmeFileNames = new HashSet<string>(
            OrderedReadmeFileNames,
            StringComparer.OrdinalIgnoreCase);

        public static bool HasReadme(this PackageArchiveReader package)
            => package.GetFiles().Any(ReadmeFileNames.Contains);

        public static Stream GetReadme(this PackageArchiveReader package)
        {
            var packageFiles = package.GetFiles();

            foreach (var readmeFileName in OrderedReadmeFileNames)
            {
                var readmePath = packageFiles.FirstOrDefault(f => f.Equals(readmeFileName, StringComparison.OrdinalIgnoreCase));

                if (readmePath != null)
                {
                    return package.GetStream(readmePath);
                }
            }

            return Stream.Null;
        }
    }
}
