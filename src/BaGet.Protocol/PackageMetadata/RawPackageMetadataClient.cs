using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// The client to interact with an upstream source's Package Metadata resource.
    /// </summary>
    public class RawPackageMetadataClient : IPackageMetadataClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _packageMetadataUrl;

        /// <summary>
        /// Create a new Package Metadata client.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="registrationBaseUrl">The NuGet server's registration resource URL.</param>
        public RawPackageMetadataClient(HttpClient httpClient, string registrationBaseUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _packageMetadataUrl = registrationBaseUrl ?? throw new ArgumentNullException(nameof(registrationBaseUrl));
        }

        /// <inheritdoc />
        public async Task<RegistrationIndexResponse> GetRegistrationIndexOrNullAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_packageMetadataUrl}/{packageId.ToLowerInvariant()}/index.json";
            var response = await _httpClient.DeserializeUrlAsync<RegistrationIndexResponse>(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }

        /// <inheritdoc />
        public async Task<RegistrationPageResponse> GetRegistrationPageAsync(
            string pageUrl,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeserializeUrlAsync<RegistrationPageResponse>(pageUrl, cancellationToken);

            return response.GetResultOrThrow();
        }

        /// <inheritdoc />
        public async Task<RegistrationLeafResponse> GetRegistrationLeafOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var id = packageId.ToLowerInvariant();
            var version = packageVersion.ToNormalizedString().ToLowerInvariant();

            var url = $"{_packageMetadataUrl}/{id}/{version}.json";
            var response = await _httpClient.DeserializeUrlAsync<RegistrationLeafResponse>(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }
    }
}
