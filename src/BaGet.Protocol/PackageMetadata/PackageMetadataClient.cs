using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol.Internal
{
    /// <summary>
    /// The client to interact with an upstream source's Package Metadata resource.
    /// </summary>
    public class PackageMetadataClient : IPackageMetadataResource
    {
        private readonly HttpClient _httpClient;
        private readonly string _packageMetadataUrl;

        /// <summary>
        /// Create a new Package Metadata client.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        /// <param name="registrationBaseUrl">The NuGet server's registration resource URL.</param>
        public PackageMetadataClient(HttpClient httpClient, string registrationBaseUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _packageMetadataUrl = registrationBaseUrl.TrimEnd('/')
                ?? throw new ArgumentNullException(nameof(registrationBaseUrl));
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
        public async Task<RegistrationPageResponse> GetRegistrationPageOrNullAsync(
            string packageId,
            NuGetVersion lower,
            NuGetVersion upper,
            CancellationToken cancellationToken = default)
        {
            var id = packageId.ToLowerInvariant();
            var lowerVersion = lower.ToNormalizedString().ToLowerInvariant();
            var upperVersion = upper.ToNormalizedString().ToLowerInvariant();

            var url = $"{_packageMetadataUrl}/{id}/page/{lowerVersion}/{upperVersion}.json";
            var response = await _httpClient.DeserializeUrlAsync<RegistrationPageResponse>(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

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
