using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public interface ISearchClient
    {
        Task<SearchResponse> GetSearchResultsAsync(string searchUrl);

        Task<AutocompleteResult> GetAutocompleteResultsAsync(string searchUrl);
    }
}
