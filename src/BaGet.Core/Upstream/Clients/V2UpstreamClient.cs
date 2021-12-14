using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BaGet.Core
{
    using ILogger = Microsoft.Extensions.Logging.ILogger<V2UpstreamClient>;
    using INuGetLogger = NuGet.Common.ILogger;

    /// <summary>
    /// The client to upstream a NuGet server that uses the V2 protocol.
    /// </summary>
    public class V2UpstreamClient : IUpstreamClient, IDisposable
    {
        private readonly SourceRepository _repository;
        private readonly ILogger _logger;

        private readonly SourceCacheContext _cache = new SourceCacheContext();
        private readonly INuGetLogger _ngLogger = NullLogger.Instance;

        public V2UpstreamClient(
            IOptionsSnapshot<MirrorOptions> options,
            ILogger logger)
        {
            var source = new PackageSource(options.Value.PackageSource.AbsoluteUri);

            _repository = Repository.Factory.GetCoreV2(source);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public V2UpstreamClient(SourceRepository repository, ILogger logger)
        {
            _repository = repository;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
        {
            try
            {
                var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                var versions = await resource.GetAllVersionsAsync(id, _cache, _ngLogger, cancellationToken);

                return versions.ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror {PackageId}'s upstream versions", id);
                return new List<NuGetVersion>();
            }
        }

        public async Task<IReadOnlyList<Package>> ListPackagesAsync(
            string id,
            CancellationToken cancellationToken)
        {
            try
            {
                var resource = await _repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
                var packages = await resource.GetMetadataAsync(
                    id,
                    includePrerelease: true,
                    includeUnlisted: true,
                    _cache,
                    _ngLogger,
                    cancellationToken);

                return packages.Select(ToPackage).ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror {PackageId}'s upstream versions", id);
                return new List<Package>();
            }
        }

        public async Task<Stream> DownloadPackageOrNullAsync(
            string id,
            NuGetVersion version,
            CancellationToken cancellationToken)
        {
            var packageStream = new MemoryStream();

            try
            {
                var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                var success = await resource.CopyNupkgToStreamAsync(
                    id, version, packageStream, _cache, _ngLogger,
                    cancellationToken);

                if (!success)
                {
                    packageStream.Dispose();
                    return null;
                }

                packageStream.Position = 0;

                return packageStream;
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Failed to index package {Id} {Version} from upstream",
                    id,
                    version);

                packageStream.Dispose();
                return null;
            }
        }

        public void Dispose() => _cache.Dispose();

        private Package ToPackage(IPackageSearchMetadata package)
        {
            return new Package
            {
                Id = package.Identity.Id,
                Version = package.Identity.Version,
                Authors = ParseAuthors(package.Authors),
                Description = package.Description,
                Downloads = 0,
                HasReadme = false,
                Language = string.Empty,
                Listed = package.IsListed,
                MinClientVersion = string.Empty,
                Published = package.Published?.UtcDateTime ?? DateTime.MinValue,
                RequireLicenseAcceptance = package.RequireLicenseAcceptance,
                Summary = package.Summary,
                Title = package.Title,
                IconUrl = package.IconUrl,
                LicenseUrl = package.LicenseUrl,
                ProjectUrl = package.ProjectUrl,
                PackageTypes = new List<PackageType>(),
                RepositoryUrl = null,
                RepositoryType = null,
                Tags = package.Tags?.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries),

                Dependencies = ToDependencies(package)
            };
        }

        private string[] ParseAuthors(string authors)
        {
            if (string.IsNullOrEmpty(authors)) return Array.Empty<string>();

            return authors
                .Split(new[] { ',', ';', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .ToArray();
        }

        private List<PackageDependency> ToDependencies(IPackageSearchMetadata package)
        {
            return package
                .DependencySets
                .SelectMany(ToDependencies)
                .ToList();
        }

        private IEnumerable<PackageDependency> ToDependencies(PackageDependencyGroup group)
        {
            var framework = group.TargetFramework.GetShortFolderName();

            // BaGet stores a dependency group with no dependencies as a package dependency
            // with no package id nor package version.
            if ((group.Packages?.Count() ?? 0) == 0)
            {
                return new[]
                {
                    new PackageDependency
                    {
                        Id = null,
                        VersionRange = null,
                        TargetFramework = framework,
                    }
                };
            }

            return group.Packages.Select(d => new PackageDependency
            {
                Id = d.Id,
                VersionRange = d.VersionRange?.ToString(),
                TargetFramework = framework,
            });
        }
    }
}
