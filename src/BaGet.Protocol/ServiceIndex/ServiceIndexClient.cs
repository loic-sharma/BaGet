using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <summary>
    /// Fetches the service index from an upstream package source.
    /// </summary>
    internal class ServiceIndexClient : IServiceIndexResource
    {
        private readonly HttpClient _httpClient;
        private readonly string _indexUrl;

        /// <summary>
        /// Create a service index for the upstream source.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="indexUrl">The upstream source's service index URL.</param>
        public ServiceIndexClient(HttpClient httpClient, string indexUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _indexUrl = indexUrl ?? throw new ArgumentNullException(nameof(indexUrl));
        }

        /// <inheritdoc />
        public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeserializeUrlAsync<ServiceIndexResponse>(_indexUrl, cancellationToken);

            return response.GetResultOrThrow();
        }
    }
}
