using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// A NuGet client that interact with a NuGet server.
    /// </summary>
    public class NuGetClient : INuGetClient
    {
        private readonly ServiceIndexClient _serviceIndexClient;
        private readonly PackageContentClient _packageContentClient;
        private readonly PackageMetadataClient _packageMetadataClient;
        private readonly SearchClient _searchClient;

        /// <summary>
        /// Configure the NuGet client.
        /// </summary>
        /// <param name="serviceIndexUrl">The NuGet Service Index resource.</param>
        public NuGetClient(string serviceIndexUrl)
            : this(httpClient: null, serviceIndexUrl: serviceIndexUrl)
        {
        }

        /// <summary>
        /// Configure the NuGet client.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to create NuGet clients.</param>
        /// <param name="serviceIndexUrl">The NuGet Service Index resource.</param>
        public NuGetClient(HttpClient httpClient, string serviceIndexUrl)
        {
            httpClient = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

            var serviceIndexClient = new ServiceIndexClient(httpClient, serviceIndexUrl);
            var urlGeneratorFactory = new AsyncUrlGenerator(serviceIndexClient);

            _serviceIndexClient = serviceIndexClient;
            _packageContentClient = new PackageContentClient(urlGeneratorFactory, httpClient);
            _packageMetadataClient = new PackageMetadataClient(urlGeneratorFactory, httpClient);
            _searchClient = new SearchClient(urlGeneratorFactory, httpClient);
        }

        public async Task<Stream> GetPackageStreamAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            var stream = await _packageContentClient.GetPackageContentStreamOrNullAsync(packageId, packageVersion, cancellationToken);
            if (stream == null)
            {
                throw new PackageNotFoundException(packageId, packageVersion);
            }

            return stream;
        }

        public async Task<Stream> GetPackageManifestStreamAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            var stream = await _packageContentClient.GetPackageManifestStreamOrNullAsync(packageId, packageVersion, cancellationToken);
            if (stream == null)
            {
                throw new PackageNotFoundException(packageId, packageVersion);
            }

            return stream;
        }

        public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersions(string packageId, CancellationToken cancellationToken)
        {
            // TODO: If server does not have autocomplete, fall back to registration.
            var request = new AutocompleteRequest
            {
                Query = packageId,
                Type = AutocompleteRequestType.PackageVersions
            };

            var results = new List<NuGetVersion>();
            var response = await _searchClient.AutocompleteAsync(request, cancellationToken);

            foreach (var versionString in response.Data)
            {
                if (!NuGetVersion.TryParse(versionString, out var version))
                {
                    continue;
                }

                results.Add(version);
            }

            return results;
        }

        public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersions(string packageId, bool includeUnlisted, CancellationToken cancellationToken = default)
        {
            if (!includeUnlisted)
            {
                return await ListPackageVersions(packageId, cancellationToken);
            }

            var response = await _packageContentClient.GetPackageVersionsOrNullAsync(packageId, cancellationToken);
            if (response == null)
            {
                return new List<NuGetVersion>();
            }

            return response.Versions;
        }

        public async Task<IReadOnlyList<RegistrationIndexPageItem>> GetPackageMetadataAsync(string packageId, CancellationToken cancellationToken = default)
        {
            var result = new List<RegistrationIndexPageItem>();

            var registrationIndex = await _packageMetadataClient.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);
            if (registrationIndex == null)
            {
                return result;
            }

            foreach (var registrationIndexPage in registrationIndex.Pages)
            {
                // If the package's registration index is too big, it will be split into registration
                // pages stored at different URLs. We will need to fetch each page's items individually.
                // We can detect this case as the registration index will have "null" items.
                var items = registrationIndexPage.ItemsOrNull;
                if (items == null)
                {
                    var externalRegistrationPage = await _packageMetadataClient.GetRegistrationPageOrNullAsync(
                        packageId,
                        registrationIndexPage.Lower,
                        registrationIndexPage.Upper,
                        cancellationToken);

                    // Skip malformed external pages.
                    if (externalRegistrationPage?.ItemsOrNull == null) continue;

                    items = externalRegistrationPage.ItemsOrNull;
                }

                result.AddRange(items);
            }

            return result;
        }

        public async Task<RegistrationIndexPageItem> GetPackageMetadataAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            var registrationIndex = await _packageMetadataClient.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);
            if (registrationIndex == null)
            {
                throw new PackageNotFoundException(packageId, packageVersion);
            }

            var result = new List<RegistrationIndexPageItem>();
            foreach (var registrationIndexPage in registrationIndex.Pages)
            {
                // Skip pages that do not contain the desired package version.
                if (registrationIndexPage.Lower > packageVersion) continue;
                if (registrationIndexPage.Upper < packageVersion) continue;

                // If the package's registration index is too big, it will be split into registration
                // pages stored at different URLs. We will need to fetch each page's items individually.
                // We can detect this case as the registration index will have "null" items.
                var items = registrationIndexPage.ItemsOrNull;
                if (items == null)
                {
                    var externalRegistrationPage = await _packageMetadataClient.GetRegistrationPageOrNullAsync(
                        packageId,
                        registrationIndexPage.Lower,
                        registrationIndexPage.Upper,
                        cancellationToken);

                    // Skip malformed external pages.
                    if (externalRegistrationPage?.ItemsOrNull == null) continue;

                    items = externalRegistrationPage.ItemsOrNull;
                }

                return items.SingleOrDefault(i => i.PackageMetadata.Version == packageVersion);
            }

            // No registration pages contained the desired version.
            throw new PackageNotFoundException(packageId, packageVersion);
        }

        public async Task<SearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            var request = new SearchRequest
            {
                Query = query,
                Skip = 0,
                Take = 20,
                IncludePrerelease = true,
                IncludeSemVer2 = true,
            };

            return await _searchClient.SearchAsync(request, cancellationToken);
        }

        public async Task<SearchResponse> SearchAsync(string query, bool includePrerelease, CancellationToken cancellationToken = default)
        {
            var request = new SearchRequest
            {
                Query = query,
                Skip = 0,
                Take = 20,
                IncludePrerelease = includePrerelease,
                IncludeSemVer2 = true,
            };

            return await _searchClient.SearchAsync(request, cancellationToken);
        }

        public async Task<SearchResponse> SearchAsync(string query, int skip, int take, CancellationToken cancellationToken = default)
        {
            var request = new SearchRequest
            {
                Query = query,
                Skip = skip,
                Take = take,
                IncludePrerelease = true,
                IncludeSemVer2 = true,
            };

            return await _searchClient.SearchAsync(request, cancellationToken);
        }

        public async Task<SearchResponse> SearchAsync(string query, bool includePrerelease, int skip, int take, CancellationToken cancellationToken = default)
        {
            var request = new SearchRequest
            {
                Query = query,
                Skip = skip,
                Take = take,
                IncludePrerelease = includePrerelease,
                IncludeSemVer2 = true,
            };

            return await _searchClient.SearchAsync(request, cancellationToken);
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(string query, CancellationToken cancellationToken = default)
        {
            var request = new AutocompleteRequest
            {
                Query = query,
                Skip = 0,
                Take = 20,
                IncludePrerelease = true,
                IncludeSemVer2 = true,
            };

            return await _searchClient.AutocompleteAsync(request, cancellationToken);
        }

        public async Task<AutocompleteResponse> AutocompleteAsync(string query, int skip, int take, CancellationToken cancellationToken = default)
        {
            var request = new AutocompleteRequest
            {
                Query = query,
                Skip = skip,
                Take = take,
                IncludePrerelease = true,
                IncludeSemVer2 = true,
            };

            return await _searchClient.AutocompleteAsync(request, cancellationToken);
        }

        /// <summary>
        /// Create a client to interact with the NuGet Service Index resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
        /// </summary>
        /// <returns>A client to interact with the NuGet Service Index resource.</returns>
        public IServiceIndexResource CreateServiceIndexClient()
            => _serviceIndexClient;

        /// <summary>
        /// Create a client to interact with the NuGet Package Content resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Content resource.</returns>
        public IPackageContentResource CreatePackageContentClient()
            => _packageContentClient;

        /// <summary>
        /// Create a client to interact with the NuGet Package Metadata resource.
        /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
        /// </summary>
        /// <returns>A client to interact with the NuGet Package Metadata resource.</returns>
        public IPackageMetadataResource CreatePackageMetadataClient()
            => _packageMetadataClient;

        /// <summary>
        /// Create a client to interact with the NuGet Search resource.
        /// </summary>
        /// <returns>A client to interact with the NuGet Search resource.</returns>
        public ISearchResource CreateSearchClient()
            => _searchClient;
    }
}
