using System;
using System.Net;
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

        public async Task<RegistrationIndex> GetRegistrationIndexOrNullAsync(string indexUrl)
        {
            var response = await _httpClient.DeserializeUrlAsync<RegistrationIndex>(indexUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }

        public async Task<RegistrationIndexPage> GetRegistrationIndexPageAsync(string pageUrl)
        {
            var response = await _httpClient.DeserializeUrlAsync<RegistrationIndexPage>(pageUrl);

            return response.GetResultOrThrow();
        }

        public async Task<RegistrationLeaf> GetRegistrationLeafAsync(string leafUrl)
        {
            var response = await _httpClient.DeserializeUrlAsync<RegistrationLeaf>(leafUrl);

            return response.GetResultOrThrow();
        }
    }
}
