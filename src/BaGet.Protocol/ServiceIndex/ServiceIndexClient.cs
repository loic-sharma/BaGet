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
        private readonly Lazy<Task<ServiceIndexResponse>> _serviceIndexTask;

        /// <summary>
        /// Create a service index for the upstream source.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="indexUrl">The upstream source's service index URL.</param>
        public ServiceIndexClient(HttpClient httpClient, string indexUrl)
        {
            _serviceIndexTask = new Lazy<Task<ServiceIndexResponse>>(async () =>
            {
                var response = await httpClient.DeserializeUrlAsync<ServiceIndexResponse>(indexUrl);

                return response.GetResultOrThrow();
            });
        }

        /// <inheritdoc />
        public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
        {
            // TODO: Observe the cancellationToken.
            return await _serviceIndexTask.Value;
        }
    }
}
