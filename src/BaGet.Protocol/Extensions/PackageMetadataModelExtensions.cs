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
        /// Determines if the provided package metadata represents a listed package.
        /// </summary>
        /// <param name="package">The package metadata.</param>
        /// <returns>True if the package is listed.</returns>
        public static bool IsListed(this PackageMetadata package)
        {
            if (package.Listed.HasValue)
            {
                return package.Listed.Value;
            }

            // A published year of 1900 indicates that this package is unlisted, when the listed property itself is
            // not present (legacy behavior).
            return package.Published.Year != 1900;
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
