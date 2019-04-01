using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <summary>
    /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
    /// </summary>
    public class ServiceIndexClient : IServiceIndexClient
    {
        private readonly HttpClient _httpClient;

        public ServiceIndexClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<ServiceIndex> GetServiceIndexAsync(string indexUrl)
        {
            var response = await _httpClient.DeserializeUrlAsync<ServiceIndex>(indexUrl);

            return response.GetResultOrThrow();
        }
    }
}
