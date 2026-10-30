using System.Collections.Generic;
using BaGet.Protocol.Models;

namespace BaGet.Core;

public interface ISearchResponseBuilder
{
    /// <summary>
    /// Build a search response.
    /// </summary>
    /// <param name="results">The page of package registrations that should be returned.</param>
    /// <param name="totalHits">
    /// The total number of packages that matched the search, disregarding skip and take.
    /// This is reported as <see cref="SearchResponse.TotalHits"/>, so it must count every
    /// match and not merely the ones on the current page.
    /// </param>
    SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> results, long totalHits);

    /// <summary>
    /// Build an autocomplete response.
    /// </summary>
    /// <param name="data">The page of results that should be returned.</param>
    /// <param name="totalHits">
    /// The total number of results that matched the query, disregarding skip and take.
    /// This is reported as <see cref="AutocompleteResponse.TotalHits"/>, so it must count
    /// every match and not merely the ones on the current page.
    /// </param>
    AutocompleteResponse BuildAutocomplete(IReadOnlyList<string?> data, long totalHits);

    DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> results);
}