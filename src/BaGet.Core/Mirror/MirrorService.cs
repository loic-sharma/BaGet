using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using BaGet.Core.Indexing;
using BaGet.Core.Metadata;
using BaGet.Protocol;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    using PackageIdentity = NuGet.Packaging.Core.PackageIdentity;

    public class MirrorService : IMirrorService
    {
        private readonly IPackageService _localPackages;
        private readonly IPackageContentResource _upstreamContent;
        private readonly IPackageMetadataResource _upstreamMetadata;
        private readonly IPackageIndexingService _indexer;
        private readonly ILogger<MirrorService> _logger;

        public MirrorService(
            IPackageService localPackages,
            IPackageContentResource upstreamContent,
            IPackageMetadataResource upstreamMetadata,
            IPackageIndexingService indexer,
            ILogger<MirrorService> logger)
        {
            _localPackages = localPackages ?? throw new ArgumentNullException(nameof(localPackages));
            _upstreamContent = upstreamContent ?? throw new ArgumentNullException(nameof(upstreamContent));
            _upstreamMetadata = upstreamMetadata ?? throw new ArgumentNullException(nameof(upstreamMetadata));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsOrNullAsync(
            string id,
            CancellationToken cancellationToken)
        {
            var response = await _upstreamContent.GetPackageVersionsOrNullAsync(id, cancellationToken);
            if (response == null)
            {
                return null;
            }

            // Merge the local package versions into the upstream package versions.
            var localPackages = await _localPackages.FindAsync(id, includeUnlisted: true);
            var localVersions = localPackages.Select(p => p.Version);

            return response.Versions.Concat(localVersions).Distinct().ToList();
        }

        public async Task<IReadOnlyList<Package>> FindPackagesOrNullAsync(string id, CancellationToken cancellationToken)
        {
            var upstreamPackageMetadata = await FindAllUpstreamMetadataOrNull(id, cancellationToken);
            if (upstreamPackageMetadata == null)
            {
                return null;
            }

            var upstreamPackages = upstreamPackageMetadata.Select(ToPackage);

            // Return the upstream packages if there are no local packages matching the package id.
            var localPackages = await _localPackages.FindAsync(id, includeUnlisted: true);
            if (!localPackages.Any())
            {
                return upstreamPackages.ToList();
            }

            // Otherwise, merge the local packages into the upstream packages.
            var result = upstreamPackages.ToDictionary(p => new PackageIdentity(p.Id, p.Version));
            var local = localPackages.ToDictionary(p => new PackageIdentity(p.Id, p.Version));

            foreach (var localPackage in local)
            {
                result[localPackage.Key] = localPackage.Value;
            }

            return result.Values.ToList();
        }

        public async Task MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            if (await _localPackages.ExistsAsync(id, version))
            {
                return;
            }

            _logger.LogInformation(
                "Package {PackageId} {PackageVersion} does not exist locally. Indexing from upstream feed...",
                id,
                version);

            await IndexFromSourceAsync(id, version, cancellationToken);

            _logger.LogInformation(
                "Finished indexing {PackageId} {PackageVersion} from the upstream feed",
                id,
                version);
        }

        private Package ToPackage(PackageMetadata metadata)
        {
            return new Package
            {
                Id = metadata.PackageId,
                Version = metadata.Version,
                Authors = ParseAuthors(metadata.Authors),
                Description = metadata.Description,
                Downloads = 0,
                HasReadme = false,
                Language = metadata.Language,
                Listed = metadata.Listed,
                MinClientVersion = metadata.MinClientVersion,
                Published = metadata.Published,
                RequireLicenseAcceptance = metadata.RequireLicenseAcceptance,
                Summary = metadata.Summary,
                Title = metadata.Title,
                IconUrl = ParseUri(metadata.IconUrl),
                LicenseUrl = ParseUri(metadata.LicenseUrl),
                ProjectUrl = ParseUri(metadata.ProjectUrl),
                PackageTypes = new List<PackageType>(),
                RepositoryUrl = null,
                RepositoryType = null,
                Tags = metadata.Tags.ToArray(),

                Dependencies = FindDependencies(metadata)
            };
        }

        private Uri ParseUri(string uriString)
        {
            if (uriString == null) return null;

            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            {
                return null;
            }

            return uri;
        }

        private string[] ParseAuthors(string authors)
        {
            if (string.IsNullOrEmpty(authors)) return new string[0];

            return authors
                .Split(new[] { ',', ';', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .ToArray();
        }

        private List<PackageDependency> FindDependencies(PackageMetadata package)
        {
            if ((package.DependencyGroups?.Count ?? 0) == 0)
            {
                return new List<PackageDependency>();
            }

            return package.DependencyGroups
                .SelectMany(FindDependenciesFromDependencyGroup)
                .ToList();
        }

        private IEnumerable<PackageDependency> FindDependenciesFromDependencyGroup(DependencyGroupItem group)
        {
            // BaGet stores a dependency group with no dependencies as a package dependency
            // with no package id nor package version.
            if ((group.Dependencies?.Count ?? 0) == 0)
            {
                return new[]
                {
                    new PackageDependency
                    {
                        Id = null,
                        VersionRange = null,
                        TargetFramework = group.TargetFramework
                    }
                };
            }

            return group.Dependencies.Select(d => new PackageDependency
            {
                Id = d.Id,
                VersionRange = d.Range,
                TargetFramework = group.TargetFramework
            });
        }

        private async Task<IReadOnlyList<PackageMetadata>> FindAllUpstreamMetadataOrNull(string id, CancellationToken cancellationToken)
        {
            var packageIndex = await _upstreamMetadata.GetRegistrationIndexOrNullAsync(id, cancellationToken);
            if (packageIndex == null)
            {
                return null;
            }

            var result = new List<PackageMetadata>();

            foreach (var page in packageIndex.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // If the package's registration index is too big, it will be split into registration
                // pages stored at different URLs. We will need to fetch each page's items individually.
                // We can detect this case as the registration index will have "null" items.
                var items = page.ItemsOrNull;

                if (items == null)
                {
                    var externalPage = await _upstreamMetadata.GetRegistrationPageOrNullAsync(id, page.Lower, page.Upper, cancellationToken);

                    if (externalPage == null || externalPage.ItemsOrNull == null)
                    {
                        // This should never happen...
                        _logger.LogError(
                            "Missing or invalid registration page for {PackageId}, versions {Lower} to {Upper}",
                            id,
                            page.Lower,
                            page.Upper);
                        continue;
                    }

                    items = externalPage.ItemsOrNull;
                }

                result.AddRange(items.Select(i => i.PackageMetadata));
            }

            return result;
        }

        private async Task IndexFromSourceAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Attempting to mirror package {PackageId} {PackageVersion}...",
                id,
                version);

            Stream packageStream = null;

            try
            {
                using (var stream = await _upstreamContent.GetPackageContentStreamOrNullAsync(id, version, cancellationToken))
                {
                    if (stream == null)
                    {
                        _logger.LogWarning(
                            "Failed to download package {PackageId} {PackageVersion}",
                            id,
                            version);

                        return;
                    }

                    packageStream = await stream.AsTemporaryFileStreamAsync();
                }

                _logger.LogInformation(
                    "Downloaded package {PackageId} {PackageVersion}, indexing...",
                    id,
                    version);

                var result = await _indexer.IndexAsync(packageStream, cancellationToken);

                _logger.LogInformation(
                    "Finished indexing package {PackageId} {PackageVersion} with result {Result}",
                    id,
                    version,
                    result);
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Failed to mirror package {PackageId} {PackageVersion}",
                    id,
                    version);

                packageStream?.Dispose();
            }
        }
    }
}
