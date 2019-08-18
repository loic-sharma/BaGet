using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The client to interact with an upstream source's Package Metadata resource.
    /// </summary>
    internal class PackageMetadataClient : IPackageMetadataResource
    {
        private readonly IAsyncUrlGenerator _urlGenerator;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Create a new Package Metadata client.
        /// </summary>
        /// <param name="urlGenerator">The service to generate URLs to upstream resources.</param>
        /// <param name="httpClient">The HTTP client used to send requests.</param>
        public PackageMetadataClient(IAsyncUrlGenerator urlGenerator, HttpClient httpClient)
        {
            _urlGenerator = urlGenerator ?? throw new ArgumentNullException(nameof(urlGenerator));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <inheritdoc />
        public async Task<RegistrationIndexResponse> GetRegistrationIndexOrNullAsync(string id, CancellationToken cancellationToken = default)
        {
            var url = await _urlGenerator.GetRegistrationIndexUrlAsync(id, cancellationToken);
            var response = await _httpClient.DeserializeUrlAsync<RegistrationIndexResponse>(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }

        /// <inheritdoc />
        public async Task<RegistrationPageResponse> GetRegistrationPageOrNullAsync(
            string id,
            NuGetVersion lower,
            NuGetVersion upper,
            CancellationToken cancellationToken = default)
        {
            var url = await _urlGenerator.GetRegistrationPageUrlAsync(id, lower, upper, cancellationToken);
            var response = await _httpClient.DeserializeUrlAsync<RegistrationPageResponse>(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }

        /// <inheritdoc />
        public async Task<RegistrationLeafResponse> GetRegistrationLeafOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken = default)
        {
            var url = await _urlGenerator.GetRegistrationLeafUrlAsync(id, version, cancellationToken);
            var response = await _httpClient.DeserializeUrlAsync<RegistrationLeafResponse>(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.GetResultOrThrow();
        }
    }
}
