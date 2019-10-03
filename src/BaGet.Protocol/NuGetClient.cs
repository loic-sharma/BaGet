using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// The <see cref="NuGetClient"/> allows you to interact with a NuGet server.
    /// </summary>
    public class NuGetClient
    {
        private readonly NuGetClientFactory _clientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="NuGetClient"/> class
        /// for mocking.
        /// </summary>
        protected NuGetClient()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NuGetClient"/> class.
        /// </summary>
        /// <param name="serviceIndexUrl">
        /// The NuGet Service Index resource URL.
        ///
        /// For NuGet.org, use https://api.nuget.org/v3/index.json
        /// </param>
        public NuGetClient(string serviceIndexUrl)
        {
            var httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

            _clientFactory = new NuGetClientFactory(httpClient, serviceIndexUrl);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NuGetClient"/> class.
        /// </summary>
        /// <param name="clientFactory">The factory used to create NuGet clients.</param>
        public NuGetClient(NuGetClientFactory clientFactory)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        /// <summary>
        /// Check if a package exists.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>Whether the package exists.</returns>
        public virtual async Task<bool> ExistsAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetPackageContentClientAsync(cancellationToken);
            var versions = await client.GetPackageVersionsOrNullAsync(packageId, cancellationToken);

            return (versions != null && versions.Versions.Any());
        }

        /// <summary>
        /// Check if a package exists.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="packageVersion">The package version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>Whether the package exists.</returns>
        public virtual async Task<bool> ExistsAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetPackageContentClientAsync(cancellationToken);
            var versions = await client.GetPackageVersionsOrNullAsync(packageId, cancellationToken);

            if (versions == null)
            {
                return false;
            }

            return versions
                .ParseVersions()
                .Any(v => v == packageVersion);
        }

        /// <summary>
        /// Download a package (.nupkg), or throws if the package does not exist.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="packageVersion">The package version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's content stream. The stream may be unseekable and may be unbuffered.</returns>
        /// <exception cref="PackageNotFoundException">
        ///     The package could not be found.
        /// </exception>
        public virtual async Task<Stream> GetPackageStreamAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetPackageContentClientAsync(cancellationToken);
            var stream = await client.GetPackageContentStreamOrNullAsync(packageId, packageVersion, cancellationToken);

            if (stream == null)
            {
                throw new PackageNotFoundException(packageId, packageVersion);
            }

            return stream;
        }

        /// <summary>
        /// Download a package's manifest (.nuspec), or throws if the package does not exist.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="packageVersion">The package version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's manifest stream. The stream may not be seekable.</returns>
        /// <exception cref="PackageNotFoundException">
        ///     The package could not be found.
        /// </exception>
        public virtual async Task<Stream> GetPackageManifestStreamAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetPackageContentClientAsync(cancellationToken);
            var stream = await client.GetPackageManifestStreamOrNullAsync(packageId, packageVersion, cancellationToken);

            if (stream == null)
            {
                throw new PackageNotFoundException(packageId, packageVersion);
            }

            return stream;
        }

        /// <summary>
        /// Find all versions of a package, excluding unlisted versions.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's listed versions, if any.</returns>
        public virtual async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string packageId, CancellationToken cancellationToken)
        {
            // TODO: Use the Autocomplete's enumerate versions endpoint if this is not Sleet.
            var packages = await GetPackageMetadataAsync(packageId, cancellationToken);

            return packages
                .Where(p => p.IsListed())
                .Select(p => p.ParseVersion())
                .ToList();
        }

        /// <summary>
        /// Find all versions of a package.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="includeUnlisted">Whether to include unlisted versions.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's versions, or an empty list if the package does not exist.</returns>
        public virtual async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string packageId, bool includeUnlisted, CancellationToken cancellationToken = default)
        {
            if (!includeUnlisted)
            {
                return await ListPackageVersionsAsync(packageId, cancellationToken);
            }

            var client = await _clientFactory.GetPackageContentClientAsync(cancellationToken);
            var response = await client.GetPackageVersionsOrNullAsync(packageId, cancellationToken);

            if (response == null)
            {
                return new List<NuGetVersion>();
            }

            return response.ParseVersions();
        }

        /// <summary>
        /// Find the metadata for all versions of a package.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's metadata, or an empty list if the package does not exist.</returns>
        public virtual async Task<IReadOnlyList<PackageMetadata>> GetPackageMetadataAsync(string packageId, CancellationToken cancellationToken = default)
        {
            var result = new List<PackageMetadata>();

            var client = await _clientFactory.GetPackageMetadataClientAsync(cancellationToken);
            var registrationIndex = await client.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);

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
                    var externalRegistrationPage = await client.GetRegistrationPageAsync(
                        registrationIndexPage.RegistrationPageUrl,
                        cancellationToken);

                    // Skip malformed external pages.
                    if (externalRegistrationPage?.ItemsOrNull == null) continue;

                    items = externalRegistrationPage.ItemsOrNull;
                }

                result.AddRange(items.Select(i => i.PackageMetadata));
            }

            return result;
        }

        /// <summary>
        /// Find the metadata for a single version of a package, or throws if the package does not exist.
        /// </summary>
        /// <param name="packageId">The package ID.</param>
        /// <param name="packageVersion">The package version.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package's metadata.</returns>
        /// <exception cref="PackageNotFoundException">
        ///     The package could not be found.
        /// </exception>
        public virtual async Task<PackageMetadata> GetPackageMetadataAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetPackageMetadataClientAsync(cancellationToken);
            var registrationIndex = await client.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);

            if (registrationIndex == null)
            {
                throw new PackageNotFoundException(packageId, packageVersion);
            }

            foreach (var registrationIndexPage in registrationIndex.Pages)
            {
                // Skip pages that do not contain the desired package version.
                var pageLowerVersion = registrationIndexPage.ParseLower();
                var pageUpperVersion = registrationIndexPage.ParseUpper();

                if (pageLowerVersion > packageVersion) continue;
                if (pageUpperVersion < packageVersion) continue;

                // If the package's registration index is too big, it will be split into registration
                // pages stored at different URLs. We will need to fetch each page's items individually.
                // We can detect this case as the registration index will have "null" items.
                var items = registrationIndexPage.ItemsOrNull;
                if (items == null)
                {
                    var externalRegistrationPage = await client.GetRegistrationPageAsync(
                        registrationIndexPage.RegistrationPageUrl,
                        cancellationToken);

                    // Skip malformed external pages.
                    if (externalRegistrationPage?.ItemsOrNull == null) continue;

                    items = externalRegistrationPage.ItemsOrNull;
                }

                // We've found the registration items that should cover the desired package.
                var result = items.SingleOrDefault(i => i.PackageMetadata.ParseVersion() == packageVersion);
                if (result == null)
                {
                    break;
                }

                return result.PackageMetadata;
            }

            // No registration pages contained the desired version.
            throw new PackageNotFoundException(packageId, packageVersion);
        }

        /// <summary>
        /// Search for packages. Includes prerelease packages.
        /// </summary>
        /// <param name="query">
        /// The search query. If <see langword="null"/>, gets default search results.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search results, including prerelease packages.</returns>
        public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query = null,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetSearchClientAsync(cancellationToken);
            var response = await client.SearchAsync(query, cancellationToken: cancellationToken);

            return response.Data;
        }

        /// <summary>
        /// Search for packages.
        /// </summary>
        /// <param name="query">
        /// The search query. If <see langword="null"/>, gets default search results.
        /// </param>
        /// <param name="includePrerelease">Whether to include prerelease packages.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search results.</returns>
        public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            bool includePrerelease,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetSearchClientAsync(cancellationToken);
            var response = await client.SearchAsync(
                query,
                includePrerelease: includePrerelease,
                cancellationToken: cancellationToken);

            return response.Data;
        }

        /// <summary>
        /// Search for packages. Includes prerelease packages.
        /// </summary>
        /// <param name="query">
        /// The search query. If <see langword="null"/>, gets default search results.
        /// </param>
        /// <param name="skip">The number of results to skip.</param>
        /// <param name="take">The number of results to include.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search results, including prerelease packages.</returns>
        public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetSearchClientAsync(cancellationToken);
            var response =  await client.SearchAsync(
                query,
                skip,
                take,
                cancellationToken: cancellationToken);

            return response.Data;
        }

        /// <summary>
        /// Search for packages.
        /// </summary>
        /// <param name="query">
        /// The search query. If <see langword="null"/>, gets default search results.
        /// </param>
        /// <param name="includePrerelease">Whether to include prerelease packages.</param>
        /// <param name="skip">The number of results to skip.</param>
        /// <param name="take">The number of results to include.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The search results, including prerelease packages.</returns>
        public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            bool includePrerelease,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetSearchClientAsync(cancellationToken);
            var response = await client.SearchAsync(
                query,
                skip,
                take,
                includePrerelease,
                includeSemVer2: true,
                cancellationToken);

            return response.Data;
        }

        /// <summary>
        /// Search for package IDs.
        /// </summary>
        /// <param name="query">
        /// The search query. If <see langword="null"/>, gets default autocomplete results.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package IDs that matched the query.</returns>
        public virtual async Task<IReadOnlyList<string>> AutocompleteAsync(
            string query = null,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetSearchClientAsync(cancellationToken);
            var response = await client.AutocompleteAsync(query, cancellationToken: cancellationToken);

            return response.Data;
        }

        /// <summary>
        /// Search for package IDs.
        /// </summary>
        /// <param name="query">
        /// The search query. If <see langword="null"/>, gets default autocomplete results.
        /// </param>
        /// <param name="skip">The number of results to skip.</param>
        /// <param name="take">The number of results to include.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The package IDs that matched the query.</returns>
        public virtual async Task<IReadOnlyList<string>> AutocompleteAsync(string query, int skip, int take, CancellationToken cancellationToken = default)
        {
            var client = await _clientFactory.GetSearchClientAsync(cancellationToken);
            var response = await client.AutocompleteAsync(
                query,
                skip: skip,
                take: take,
                cancellationToken: cancellationToken);

            return response.Data;
        }
    }
}
