using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol
{
    public partial class NuGetClientFactory
    {
        private class SearchClient : ISearchClient
        {
            private readonly NuGetClientFactory _clientfactory;

            public SearchClient(NuGetClientFactory clientFactory)
            {
                _clientfactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            }

            public async Task<SearchResponse> SearchAsync(
                string query = null,
                int skip = 0,
                int take = 20,
                bool includePrerelease = true,
                bool includeSemVer2 = true,
                CancellationToken cancellationToken = default)
            {
                // TODO: Support search failover.
                // See: https://github.com/loic-sharma/BaGet/issues/314
                var client = await _clientfactory.GetSearchClientAsync(cancellationToken);

                return await client.SearchAsync(query, skip, take, includePrerelease, includeSemVer2);
            }
        }
    }
}
