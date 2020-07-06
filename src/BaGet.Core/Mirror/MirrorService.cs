using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using BaGet.Protocol.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core
{
    using PackageIdentity = NuGet.Packaging.Core.PackageIdentity;

    public class MirrorService : IMirrorService
    {
        private readonly IPackageService _localPackages;
        private readonly NuGetClient _upstreamClient;
        private readonly IPackageIndexingService _indexer;
        private readonly ILogger<MirrorService> _logger;

        public MirrorService(
            IPackageService localPackages,
            NuGetClient upstreamClient,
            IPackageIndexingService indexer,
            ILogger<MirrorService> logger)
        {
            _localPackages = localPackages ?? throw new ArgumentNullException(nameof(localPackages));
            _upstreamClient = upstreamClient ?? throw new ArgumentNullException(nameof(upstreamClient));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsOrNullAsync(
            string id,
            CancellationToken cancellationToken)
        {
            var upstreamVersions = await RunOrNull(
                id,
                "versions",
                () => _upstreamClient.ListPackageVersionsAsync(id, includeUnlisted: true, cancellationToken));

            if (upstreamVersions == null || !upstreamVersions.Any())
            {
                return null;
            }

            // Merge the local package versions into the upstream package versions.
            var localPackages = await _localPackages.FindAsync(id, includeUnlisted: true, cancellationToken);
            var localVersions = localPackages.Select(p => p.Version);

            return upstreamVersions.Concat(localVersions).Distinct().ToList();
        }

        public async Task<IReadOnlyList<Package>> FindPackagesOrNullAsync(string id, CancellationToken cancellationToken)
        {
            var items = await RunOrNull(
                id,
                "metadata",
                () => _upstreamClient.GetPackageMetadataAsync(id, cancellationToken));

            if (items == null || !items.Any())
            {
                return null;
            }

            return items.Select(ToPackage).ToList();
        }

        public async Task MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            if (await _localPackages.ExistsAsync(id, version, cancellationToken))
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
                Version = metadata.ParseVersion(),
                Authors = ParseAuthors(metadata.Authors),
                Description = metadata.Description,
                Downloads = 0,
                HasReadme = false,
                Language = metadata.Language,
                Listed = metadata.IsListed(),
                MinClientVersion = metadata.MinClientVersion,
                Published = metadata.Published.UtcDateTime,
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

        private async Task<T> RunOrNull<T>(string id, string data, Func<Task<T>> x)
            where T : class
        {
            try
            {
                return await x();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to mirror package {Package}'s upstream {Data}", id, data);
                return null;
            }
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
                using (var stream = await _upstreamClient.DownloadPackageAsync(id, version, cancellationToken))
                {
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
            catch (PackageNotFoundException)
            {
                _logger.LogWarning(
                    "Failed to download package {PackageId} {PackageVersion}",
                    id,
                    version);

                return;
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Failed to mirror package {PackageId} {PackageVersion}",
                    id,
                    version);
            }
            finally
            {
                packageStream?.Dispose();
            }
        }
    }
}
