using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The client to interact with an upstream source's Package Content resource.
    /// </summary>
    public class PackageContentClient : IPackageContentService
    {
        private readonly IUrlGeneratorFactory _urlGenerator;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Create a new Package Content client.
        /// </summary>
        /// <param name="urlGenerator">The service to generate URLs to upstream resources.</param>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        public PackageContentClient(IUrlGeneratorFactory urlGenerator, HttpClient httpClient)
        {
            _urlGenerator = urlGenerator ?? throw new ArgumentNullException(nameof(urlGenerator));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <inheritdoc />
        public async Task<PackageVersionsResponse> GetPackageVersionsOrNullAsync(string id, CancellationToken cancellationToken = default)
        {
            var urlGenerator = await _urlGenerator.CreateAsync();
            var url = urlGenerator.GetPackageVersionsUrl(id);
            var response = await _httpClient.DeserializeUrlAsync<PackageVersionsResponse>(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }

        /// <inheritdoc />
        public async Task<Stream> GetPackageContentStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
        {
            var urlGenerator = await _urlGenerator.CreateAsync();
            var url = urlGenerator.GetPackageDownloadUrl(id, version);
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return await response.Content.ReadAsStreamAsync();
        }

        /// <inheritdoc />
        public async Task<Stream> GetPackageManifestStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
        {
            var urlGenerator = await _urlGenerator.CreateAsync();
            var url = urlGenerator.GetPackageManifestDownloadUrl(id, version);
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return await response.Content.ReadAsStreamAsync();
        }
    }
}
