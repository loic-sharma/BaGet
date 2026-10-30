using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Internal;
using BaGet.Protocol.Models;

namespace BaGet.Protocol;

public partial class NuGetClientFactory
{
    private class AutocompleteClient : IAutocompleteClient
    {
        private readonly NuGetClientFactory _clientfactory;

        public AutocompleteClient(NuGetClientFactory clientFactory)
        {
            _clientfactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(
            string? query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            // The service index may advertise several autocomplete endpoints. Try them in order.
            // See: https://github.com/loic-sharma/BaGet/issues/314
            var clients = await _clientfactory.GetAutocompleteClientsAsync(cancellationToken);

            return await EndpointFailover.ExecuteAsync(
                clients,
                (client, token) => client.AutocompleteAsync(query, skip, take, includePrerelease, includeSemVer2, token),
                cancellationToken);
        }

        public async Task<AutocompleteResponse> ListPackageVersionsAsync(
            string packageId,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            CancellationToken cancellationToken = default)
        {
            // The service index may advertise several autocomplete endpoints. Try them in order.
            // See: https://github.com/loic-sharma/BaGet/issues/314
            var clients = await _clientfactory.GetAutocompleteClientsAsync(cancellationToken);

            return await EndpointFailover.ExecuteAsync(
                clients,
                (client, token) => client.ListPackageVersionsAsync(packageId, includePrerelease, includeSemVer2, token),
                cancellationToken);
        }
    }
}