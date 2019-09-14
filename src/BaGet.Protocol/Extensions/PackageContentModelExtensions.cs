using System.Collections.Generic;
using System.Linq;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    public static class PackageContentModelExtensions
    {
        /// <summary>
        /// Parse the package versions as <see cref="NuGetVersion" />s.
        /// </summary>
        /// <param name="response">The package versions response.</param>
        /// <returns>The package versions.</returns>
        public static IReadOnlyList<NuGetVersion> ParseVersions(this PackageVersionsResponse response)
        {
            return response
                .Versions
                .Select(NuGetVersion.Parse)
                .ToList();
        }
    }
}
