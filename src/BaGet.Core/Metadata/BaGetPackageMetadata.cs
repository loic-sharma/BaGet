using System.Collections.Generic;
using BaGet.Protocol.Models;

namespace BaGet.Core
{
    /// <summary>
    /// BaGet's extensions to the package metadata model. These additions
    /// are not part of the official protocol.
    /// </summary>
    public class BaGetPackageMetadata : PackageMetadata
    {
        public long Downloads { get; set; }
        public bool HasReadme { get; set; }
        public IReadOnlyList<string> PackageTypes { get; set; }
        public string RepositoryUrl { get; set; }
        public string RepositoryType { get; set; }
    }
}
