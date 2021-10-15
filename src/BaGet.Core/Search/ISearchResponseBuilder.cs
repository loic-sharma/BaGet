using System.Collections.Generic;
using BaGet.Protocol.Models;

namespace BaGet.Core
{
    public interface ISearchResponseBuilder
    {
        SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> results);
        AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data);
        DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> results);
    }
}
