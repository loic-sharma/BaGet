using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    /// <inheritdoc />
    public class PackageContentClient : IPackageContentClient
    {
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Create a new Package Content client.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        public PackageContentClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <inheritdoc />
        public async Task<PackageVersions> GetPackageVersionsOrNullAsync(string url)
        {
            var response = await _httpClient.DeserializeUrlAsync<PackageVersions>(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }

        /// <inheritdoc />
        public async Task<Stream> GetPackageContentStreamAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync();
        }

        /// <inheritdoc />
        public async Task<Stream> GetPackageManifestStreamAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync();
        }
    }
}
