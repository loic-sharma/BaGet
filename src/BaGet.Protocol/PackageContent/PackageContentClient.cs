using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// The client to interact with an upstream source's Package Content resource.
    /// </summary>
    public class PackageContentClient : IPackageContentResource
    {
        private readonly HttpClient _httpClient;
        private readonly string _packageContentUrl;

        /// <summary>
        /// Create a new Package Content client.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="packageContentUrl">The NuGet Server's package content URL.</param>
        public PackageContentClient(HttpClient httpClient, string packageContentUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _packageContentUrl = packageContentUrl?.TrimEnd('/')
                ?? throw new ArgumentNullException(nameof(packageContentUrl));
        }

        /// <inheritdoc />
        public async Task<PackageVersionsResponse> GetPackageVersionsOrNullAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var id = packageId.ToLowerInvariant();

            var url = $"{_packageContentUrl}/{id}/index.json";
            var response = await _httpClient.DeserializeUrlAsync<PackageVersionsResponse>(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }

        /// <inheritdoc />
        public async Task<Stream> GetPackageContentStreamOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var id = packageId.ToLowerInvariant();
            var version = packageVersion.ToNormalizedString().ToLowerInvariant();

            var url = $"{_packageContentUrl}/{id}/{version}/{id}.{version}.nupkg";
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return await response.Content.ReadAsStreamAsync();
        }

        /// <inheritdoc />
        public async Task<Stream> GetPackageManifestStreamOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var id = packageId.ToLowerInvariant();
            var version = packageVersion.ToNormalizedString().ToLowerInvariant();

            var url = $"{_packageContentUrl}/{id}/{version}/{id}.nuspec";
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return await response.Content.ReadAsStreamAsync();
        }
    }
}
