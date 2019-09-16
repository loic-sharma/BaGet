using System;
using NuGet.Versioning;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// An exception thrown when a package could not be found on the NuGet server.
    /// </summary>
    public class PackageNotFoundException : Exception
    {
        /// <summary>
        /// Create a new instance of the <see cref="PackageNotFoundException"/>.
        /// </summary>
        /// <param name="packageId">The ID of the package that could not be found.</param>
        /// <param name="packageVersion">The version of the package that could not be found.</param>
        public PackageNotFoundException(string packageId, NuGetVersion packageVersion)
            : base($"Could not find package {packageId} {packageVersion}")
        {
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            PackageVersion = packageVersion ?? throw new ArgumentNullException(nameof(packageVersion));
        }

        /// <summary>
        /// The package ID that could not be found.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// The package version that could not be found.
        /// </summary>
        public NuGetVersion PackageVersion { get; }
    }
}
