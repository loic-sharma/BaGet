using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// Fetches the service index from an upstream package source.
    /// </summary>
    public class ServiceIndexClient : IServiceIndexResource
    {
        private readonly HttpClient _httpClient;
        private readonly string _serviceIndexUrl;

        /// <summary>
        /// Create a service index for the upstream source.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="serviceIndexUrl">The NuGet server's service index URL.</param>
        public ServiceIndexClient(HttpClient httpClient, string serviceIndexUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _serviceIndexUrl = serviceIndexUrl ?? throw new ArgumentNullException(nameof(serviceIndexUrl));
        }

        /// <inheritdoc />
        public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeserializeUrlAsync<ServiceIndexResponse>(
                _serviceIndexUrl,
                cancellationToken);

            return response.GetResultOrThrow();
        }
    }
}
