using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// These are documented interpretations of values returned by the
    /// Package Metadata resource API.
    /// </summary>
    public static class RegistrationModelExtensions
    {
        /// <summary>
        /// Parse the package version as a <see cref="NuGetVersion" />.
        /// </summary>
        /// <param name="package">The package metadata.</param>
        /// <returns>The package version.</returns>
        public static NuGetVersion ParseVersion(this PackageMetadata package)
        {
            return NuGetVersion.Parse(package.Version);
        }

        /// <summary>
        /// Parse the registration page's lower version as a <see cref="NuGetVersion" />.
        /// </summary>
        /// <param name="page">The registration page.</param>
        /// <returns>The page's lower version.</returns>
        public static NuGetVersion ParseLower(this RegistrationIndexPage page)
        {
            return NuGetVersion.Parse(page.Lower);
        }

        /// <summary>
        /// Parse the registration page's upper version as a <see cref="NuGetVersion" />.
        /// </summary>
        /// <param name="page">The registration page.</param>
        /// <returns>The page's upper version.</returns>
        public static NuGetVersion ParseUpper(this RegistrationIndexPage page)
        {
            return NuGetVersion.Parse(page.Upper);
        }
    }
}
