using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol.Internal
{
    public class NullAutocompleteClient : IAutocompleteClient
    {
        public Task<AutocompleteResponse> AutocompleteAsync(string query = null, int skip = 0, int take = 20, bool includePrerelease = true, bool includeSemVer2 = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AutocompleteResponse
            {
                TotalHits = 0,
                Data = new List<string>()
            });
        }

        public Task<AutocompleteResponse> ListPackageVersionsAsync(string packageId, bool includePrerelease = true, bool includeSemVer2 = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AutocompleteResponse
            {
                TotalHits = 0,
                Data = new List<string>()
            });
        }
    }
}
