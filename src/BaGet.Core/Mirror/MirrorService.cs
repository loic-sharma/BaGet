using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using BaGet.Protocol;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core.Mirror
{
    using PackageIdentity = NuGet.Packaging.Core.PackageIdentity;

    public class MirrorService : IMirrorService
    {
        private readonly IPackageService _localPackages;
        private readonly IPackageMetadataService _upstreamFeed;
        private readonly IPackageDownloader _downloader;
        private readonly IPackageIndexingService _indexer;
        private readonly ILogger<MirrorService> _logger;

        public MirrorService(
            IPackageService localPackages,
            IPackageMetadataService upstreamFeed,
            IPackageDownloader downloader,
            IPackageIndexingService indexer,
            ILogger<MirrorService> logger)
        {
            _localPackages = localPackages ?? throw new ArgumentNullException(nameof(localPackages));
            _upstreamFeed = upstreamFeed ?? throw new ArgumentNullException(nameof(upstreamFeed));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsOrNullAsync(
            string id,
            CancellationToken cancellationToken)
        {
            var includeUnlisted = true;
            var versions = await _upstreamFeed.GetAllVersionsOrNullAsync(id, includeUnlisted, cancellationToken);
            if (versions == null)
            {
                return null;
            }

            // Merge the local package versions into the upstream package versions.
            var localPackages = await _localPackages.FindAsync(id, includeUnlisted);
            var localVersions = localPackages.Select(p => p.Version);

            return versions.Concat(localVersions).Distinct().ToList();
        }

        public async Task<IReadOnlyList<Package>> FindPackagesOrNullAsync(string id, CancellationToken cancellationToken)
        {
            var upstreamPackageMetadata = await _upstreamFeed.GetAllMetadataOrNullAsync(id, cancellationToken);
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
                Downloads = metadata.Downloads,
                HasReadme = metadata.HasReadme,
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
                PackageTypes = ParsePackageTypes(metadata.PackageTypes),
                RepositoryUrl = ParseUri(metadata.RepositoryUrl),
                RepositoryType = metadata.RepositoryType,
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

        private List<PackageType> ParsePackageTypes(IReadOnlyList<string> packageTypes)
        {
            if (packageTypes == null || packageTypes.Count == 0)
            {
                return new List<PackageType>();
            }

            return packageTypes
                .Select(t => new PackageType { Name = t, Version = "0.0.0" })
                .ToList();
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

        private async Task IndexFromSourceAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Attempting to mirror package {PackageId} {PackageVersion}...",
                id,
                version);

            try
            {
                var packageUri = await _upstreamFeed.GetPackageContentUriAsync(id, version);

                using (var stream = await _downloader.DownloadOrNullAsync(packageUri, cancellationToken))
                {
                    if (stream == null)
                    {
                        _logger.LogWarning(
                            "Failed to download package {PackageId} {PackageVersion}",
                            id,
                            version);

                        return;
                    }

                    _logger.LogInformation(
                        "Downloaded package {PackageId} {PackageVersion}, indexing...",
                        id,
                        version);

                    var result = await _indexer.IndexAsync(stream, cancellationToken);

                    _logger.LogInformation(
                        "Finished indexing package {PackageId} {PackageVersion} with result {Result}",
                        packageUri,
                        result);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Failed to mirror package {PackageId} {PackageVersion}",
                    id,
                    version);
            }
        }
    }
}
