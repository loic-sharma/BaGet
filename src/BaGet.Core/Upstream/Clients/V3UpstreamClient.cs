using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using BaGet.Protocol.Models;
using NuGet.Versioning;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    /// <summary>
    /// The mirroring client for a NuGet server that uses the V3 protocol.
    /// </summary>
    public class V3UpstreamClient : IUpstreamClient
    {
        private readonly NuGetClient _client;
        private readonly ILogger<V3UpstreamClient> _logger;

        public V3UpstreamClient(NuGetClient client, ILogger<V3UpstreamClient> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Stream> DownloadPackageOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken)
        {
            try
            {
                using (var downloadStream = await _client.DownloadPackageAsync(id, version, cancellationToken))
                {
                    return await downloadStream.AsTemporaryFileStreamAsync(cancellationToken);
                }
            }
            catch (PackageNotFoundException)
            {
                return null;
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Failed to download {PackageId} {PackageVersion} from upstream",
                    id,
                    version);
                return null;
            }
        }

        public async Task<IReadOnlyList<Package>> ListPackagesAsync(
            string id,
            CancellationToken cancellationToken)
        {
            try
            {
                var packages = await _client.GetPackageMetadataAsync(id, cancellationToken);

                return packages.Select(ToPackage).ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror {PackageId}'s upstream metadata", id);
                return new List<Package>();
            }
        }

        public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(
            string id,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _client.ListPackageVersionsAsync(id, includeUnlisted: true, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror {PackageId}'s upstream versions", id);
                return new List<NuGetVersion>();
            }
        }

        private Package ToPackage(PackageMetadata metadata)
        {
            var version = metadata.ParseVersion();

            return new Package
            {
                Id = metadata.PackageId,
                Version = version,
                Authors = ParseAuthors(metadata.Authors),
                Description = metadata.Description,
                Downloads = 0,
                HasReadme = false,
                IsPrerelease = version.IsPrerelease,
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
                SemVerLevel = version.IsSemVer2 ? SemVerLevel.SemVer2 : SemVerLevel.Unknown,
                Tags = metadata.Tags?.ToArray() ?? Array.Empty<string>(),

                Dependencies = ToDependencies(metadata)
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

        private List<PackageDependency> ToDependencies(PackageMetadata package)
        {
            if ((package.DependencyGroups?.Count ?? 0) == 0)
            {
                return new List<PackageDependency>();
            }

            return package.DependencyGroups
                .SelectMany(ToDependencies)
                .ToList();
        }

        private IEnumerable<PackageDependency> ToDependencies(DependencyGroupItem group)
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
                        TargetFramework = group.TargetFramework,
                    }
                };
            }

            return group.Dependencies.Select(d => new PackageDependency
            {
                Id = d.Id,
                VersionRange = d.Range,
                TargetFramework = group.TargetFramework,
            });
        }
    }
}
