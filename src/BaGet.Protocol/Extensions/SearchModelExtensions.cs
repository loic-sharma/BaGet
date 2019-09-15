using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// These are documented interpretations of values returned by the Search resource.
    /// </summary>
    public static class SearchModelExtensions
    {
        /// <summary>
        /// Parse the search result's version as a <see cref="NuGetVersion" />.
        /// </summary>
        /// <param name="result">The search result.</param>
        /// <returns>The search result's version.</returns>
        public static NuGetVersion ParseVersion(this SearchResult result)
        {
            return NuGetVersion.Parse(result.Version);
        }

        /// <summary>
        /// Parse the search result's version as a <see cref="NuGetVersion" />.
        /// </summary>
        /// <param name="result">The search result.</param>
        /// <returns>The search result's version.</returns>
        public static NuGetVersion ParseVersion(this SearchResultVersion result)
        {
            return NuGetVersion.Parse(result.Version);
        }
    }
}
