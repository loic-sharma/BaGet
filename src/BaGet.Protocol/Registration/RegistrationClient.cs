using System;
using System.Net.Http;

namespace BaGet.Protocol
{
    public class RegistrationClient
    {
        private readonly HttpClient _httpClient;

        public RegistrationClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }
    }
}
