using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BaGet.Core
{
    using ILogger = Microsoft.Extensions.Logging.ILogger<MirrorV2Client>;
    using INuGetLogger = NuGet.Common.ILogger;

    /// <summary>
    /// The mirroring client for a NuGet server that uses the V2 protocol.
    /// </summary>
    internal sealed class MirrorV2Client : IMirrorClient, IDisposable
    {
        private readonly SourceCacheContext _cache;
        private readonly SourceRepository _repository;
        private readonly INuGetLogger _ngLogger;
        private readonly ILogger _logger;

        public MirrorV2Client(
            IOptionsSnapshot<MirrorOptions> options,
            ILogger logger)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Value?.PackageSource?.AbsolutePath == null)
            {
                throw new ArgumentException("No mirror package source has been set.");
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _ngLogger = NullLogger.Instance;
            _cache = new SourceCacheContext();
            _repository = Repository.Factory.GetCoreV2(new PackageSource(options.Value.PackageSource.AbsoluteUri));
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

        public async Task<IReadOnlyList<PackageMetadata>> GetPackageMetadataAsync(string id, CancellationToken cancellationToken)
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

                return packages
                    .Select(package => new PackageMetadata
                    {
                        Authors = package.Authors,
                        Description = package.Description,
                        IconUrl = package.IconUrl?.AbsoluteUri,
                        LicenseUrl = package.LicenseUrl?.AbsoluteUri,
                        Listed = package.IsListed,
                        PackageId = id,
                        Summary = package.Summary,
                        Version = package.Identity.Version.ToString(),
                        Tags = package.Tags?.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries),
                        Title = package.Title,
                        RequireLicenseAcceptance = package.RequireLicenseAcceptance,
                        Published = package.Published?.UtcDateTime ?? DateTimeOffset.MinValue,
                        ProjectUrl = package.ProjectUrl?.AbsoluteUri,
                        DependencyGroups = ToDependencyGroups(package),
                    })
                    .ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to mirror {PackageId}'s upstream versions", id);
                return new List<PackageMetadata>();
            }
        }

        public async Task<Stream> DownloadPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            var packageStream = new MemoryStream();
            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            var success = await resource.CopyNupkgToStreamAsync(
                id, version, packageStream, _cache, _ngLogger,
                cancellationToken);

            if (!success)
            {
                throw new PackageNotFoundException(id, version);
            }

            packageStream.Seek(0, SeekOrigin.Begin);

            return packageStream;
        }

        public void Dispose() => _cache.Dispose();

        private IReadOnlyList<DependencyGroupItem> ToDependencyGroups(IPackageSearchMetadata package)
        {
            var groupItems = new List<DependencyGroupItem>();
            foreach (var set in package.DependencySets)
            {
                var item = new DependencyGroupItem
                {
                    TargetFramework = set.TargetFramework.Framework,
                    Dependencies = new List<DependencyItem>()
                };

                foreach (var dependency in set.Packages)
                {
                    item.Dependencies.Add(new DependencyItem
                    {
                        Id = dependency.Id,
                        Range = dependency.VersionRange.ToNormalizedString(),
                    });
                }

                groupItems.Add(item);
            }

            return groupItems;
        }
    }
}
