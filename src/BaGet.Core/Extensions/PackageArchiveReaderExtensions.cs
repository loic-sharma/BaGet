using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        public async static Task<Stream> GetReadmeAsync(
            this PackageArchiveReader package,
            CancellationToken cancellationToken)
        {
            var packageFiles = package.GetFiles();

            foreach (var readmeFileName in OrderedReadmeFileNames)
            {
                var readmePath = packageFiles.FirstOrDefault(f => f.Equals(readmeFileName, StringComparison.OrdinalIgnoreCase));

                if (readmePath != null)
                {
                    return await package.GetStreamAsync(readmePath, cancellationToken);
                }
            }

            throw new InvalidOperationException("Package does not have a readme!");
        }
    }
}
