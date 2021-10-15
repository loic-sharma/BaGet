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
    public class MirrorService : IMirrorService
    {
        private readonly IPackageService _localPackages;
        private readonly IMirrorClient _upstreamClient;
        private readonly IPackageIndexingService _indexer;
        private readonly ILogger<MirrorService> _logger;

        public MirrorService(
            IPackageService localPackages,
            IMirrorClient upstreamClient,
            IPackageIndexingService indexer,
            ILogger<MirrorService> logger)
        {
            _localPackages = localPackages ?? throw new ArgumentNullException(nameof(localPackages));
            _upstreamClient = upstreamClient ?? throw new ArgumentNullException(nameof(upstreamClient));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(
            string id,
            CancellationToken cancellationToken)
        {
            var upstreamVersions = await _upstreamClient.ListPackageVersionsAsync(id, cancellationToken);

            // Merge the local package versions into the upstream package versions.
            var localPackages = await _localPackages.FindAsync(id, includeUnlisted: true, cancellationToken);
            var localVersions = localPackages.Select(p => p.Version);

            if (!upstreamVersions.Any()) return localVersions.ToList();
            if (!localPackages.Any()) return upstreamVersions;

            return upstreamVersions.Concat(localVersions).Distinct().ToList();
        }

        public async Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken)
        {
            var upstreamPackageMetadata = await _upstreamClient.GetPackageMetadataAsync(id, cancellationToken);

            var upstreamPackages = upstreamPackageMetadata.Select(ToPackage).ToList();
            var localPackages = await _localPackages.FindAsync(id, includeUnlisted: true, cancellationToken);

            if (!upstreamPackages.Any()) return localPackages;
            if (!localPackages.Any()) return upstreamPackages;

            // Merge the local packages into the upstream packages.
            var result = upstreamPackages.ToDictionary(p => p.Version);
            var local = localPackages.ToDictionary(p => p.Version);

            foreach (var localPackage in local)
            {
                result[localPackage.Key] = localPackage.Value;
            }

            return result.Values.ToList();
        }

        public async Task<Package> FindPackageOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken)
        {
            await MirrorAsync(id, version, cancellationToken);

            return await _localPackages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
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
                Tags = metadata.Tags?.ToArray() ?? Array.Empty<string>(),

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
            if (string.IsNullOrEmpty(authors)) return Array.Empty<string>();

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
                    packageStream = await stream.AsTemporaryFileStreamAsync(cancellationToken);
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
