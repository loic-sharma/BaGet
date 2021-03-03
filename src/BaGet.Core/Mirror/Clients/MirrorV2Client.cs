using BaGet.Protocol.Models;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core
{
    internal sealed class MirrorV2Client : IMirrorNuGetClient
    {
        private readonly ILogger _logger;
        private readonly SourceCacheContext _cache;
        private readonly SourceRepository _repository;

        public MirrorV2Client(IOptionsSnapshot<MirrorOptions> options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Value?.PackageSource?.AbsolutePath == null)
            {
                throw new ArgumentException("No mirror package source has been set.");
            }

            _logger = NullLogger.Instance;
            _cache = new SourceCacheContext();
            _repository = Repository.Factory.GetCoreV2(new PackageSource(options.Value.PackageSource.AbsoluteUri));
        }

        public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, bool includeUnlisted, CancellationToken cancellationToken)
        {
            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            var versions = await resource.GetAllVersionsAsync(id, _cache, _logger, cancellationToken);

            return versions.ToList();
        }

        public async Task<IReadOnlyList<PackageMetadata>> GetPackageMetadataAsync(string id, CancellationToken cancellationToken)
        {
            var resource = await _repository.GetResourceAsync<PackageMetadataResource>();
            var packages = await resource.GetMetadataAsync(id, includePrerelease: true, includeUnlisted: false, _cache, _logger, cancellationToken);

            var result = new List<PackageMetadata>();
            foreach (var package in packages)
            {
                result.Add(new PackageMetadata
                {
                    Authors = package.Authors,
                    Description = package.Description,
                    IconUrl = package.IconUrl?.AbsoluteUri,
                    LicenseUrl = package.LicenseUrl?.AbsoluteUri,
                    Listed = package.IsListed,
                    PackageId = id,
                    Summary = package.Summary,
                    Version = package.Identity.Version.ToString(),
                    Tags = package.Tags?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                    Title = package.Title,
                    RequireLicenseAcceptance = package.RequireLicenseAcceptance,
                    Published = package.Published?.UtcDateTime ?? DateTimeOffset.MinValue,
                    ProjectUrl = package.ProjectUrl?.AbsoluteUri,
                    DependencyGroups = GetDependencies(package),
                });
            }

            return result;
        }

        public async Task<Stream> DownloadPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        {
            var packageStream = new MemoryStream();
            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            await resource.CopyNupkgToStreamAsync(id, version, packageStream, _cache, _logger, cancellationToken);
            packageStream.Seek(0, SeekOrigin.Begin);

            return packageStream;
        }

        private IReadOnlyList<DependencyGroupItem> GetDependencies(IPackageSearchMetadata package)
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
