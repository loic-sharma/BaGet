using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public class RegistrationClient : IRegistrationClient
    {
        private readonly HttpClient _httpClient;

        public RegistrationClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<RegistrationIndex> GetRegistrationIndexAsync(string indexUrl)
        {
            var response = await _httpClient.GetAsync(indexUrl);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsAsync<RegistrationIndex>();
        }

        public async Task<RegistrationIndexPage> GetRegistrationIndexPageAsync(string pageUrl)
        {
            var response = await _httpClient.GetAsync(pageUrl);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsAsync<RegistrationIndexPage>();
        }

        public async Task<RegistrationLeaf> GetRegistrationLeafAsync(string leafUrl)
        {
            var response = await _httpClient.GetAsync(leafUrl);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsAsync<RegistrationLeaf>();
        }
    }
}
