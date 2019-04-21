using System;
using System.Collections.Generic;
using BaGet.Protocol.Converters;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The full list of versions for a package.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#enumerate-package-versions
    /// Example: https://api.nuget.org/v3-flatcontainer/newtonsoft.json/index.json
    /// </summary>
    public class PackageVersions
    {
        /// <summary>
        /// Create list of versions.
        /// </summary>
        /// <param name="versions">The versions.</param>
        public PackageVersions(IReadOnlyList<NuGetVersion> versions)
        {
            Versions = versions ?? throw new ArgumentNullException(nameof(versions));
        }

        /// <summary>
        /// The versions.
        /// </summary>
        [JsonConverter(typeof(NuGetVersionListConverter))]
        public IReadOnlyList<NuGetVersion> Versions { get; }
    }
}
