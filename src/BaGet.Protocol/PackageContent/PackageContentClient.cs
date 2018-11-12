using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    public class PackageContentClient : IPackageContentClient
    {
        private readonly HttpClient _httpClient;

        public PackageContentClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Get a package's versions, or null if the package does not exist.
        /// </summary>
        /// <param name="url">The URL to fetch the package versions from.</param>
        /// <returns>The package's versions, or null if the package does not exist</returns>
        public async Task<PackageVersions> GetPackageVersionsOrNullAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsAsync<PackageVersions>();
        }

        public async Task<Stream> GetPackageContentStreamAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync();
        }

        public async Task<Stream> GetPackageManifestStreamAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync();
        }
    }
}
