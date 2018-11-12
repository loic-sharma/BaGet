using System;
using System.Net.Http;

namespace BaGet.Protocol
{
    public class FlatContainerClient
    {
        private readonly HttpClient _httpClient;

        public FlatContainerClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }
    }
}
