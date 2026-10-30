using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Protocol;

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
        #pragma warning disable CS8604
        return NuGetVersion.Parse(result.Version);
        #pragma warning restore CS8604
    }

    /// <summary>
    /// Parse the search result's version as a <see cref="NuGetVersion" />.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>The search result's version.</returns>
    public static NuGetVersion ParseVersion(this SearchResultVersion result)
    {
        #pragma warning disable CS8604
        return NuGetVersion.Parse(result.Version);
        #pragma warning restore CS8604
    }
}