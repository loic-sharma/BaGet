using System.Collections.Generic;
using BaGetter.Protocol.Models;

namespace BaGetter.Core
{
    public interface ISearchResponseBuilder
    {
        SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> results);
        AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data);
        DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> results);
    }
}
