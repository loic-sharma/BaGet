using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace BaGet.Core.Services
{
    using BagetPackageDependencyGroup = Entities.PackageDependencyGroup;
    using BaGetPackageDependency = Entities.PackageDependency;

    public class IndexingService : IIndexingService
    {
        private readonly IPackageService _packages;
        private readonly IPackageStorageService _storage;
        private readonly ILogger<IndexingService> _logger;

        public IndexingService(
            IPackageService packages,
            IPackageStorageService storage,
            ILogger<IndexingService> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        public async Task<IndexingResult> IndexAsync(Stream stream)
        {
            Package package;

            try
            {
                using (var packageReader = new PackageArchiveReader(stream))
                {
                    var packageId = packageReader.NuspecReader.GetId();
                    var packageVersion = packageReader.NuspecReader.GetVersion();

                    if (await _packages.ExistsAsync(packageId, packageVersion))
                    {
                        return IndexingResult.PackageAlreadyExists;
                    }

                    try
                    {
                        await _storage.SaveAsync(packageReader, stream);
                    }
                    catch (Exception e)
                    {
                        // This may happen due to concurrent pushes.
                        // TODO: Make IStorageService.SaveAsync return a result enum so this can be properly handled.
                        _logger.LogError(e, "Failed to save package {Identity}", packageReader.GetIdentity());

                        throw;
                    }

                    package = GetPakageMetadata(packageReader);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Uploaded package is invalid");

                return IndexingResult.InvalidPackage;
            }

            try
            {
                await _packages.AddAsync(package);

                return IndexingResult.Success;
            }
            catch (DbUpdateException e) when (e.IsUniqueConstraintViolationException())
            {
                _logger.LogError(e,
                    "Failed to upload package {PackageId} {PackageVersion} as it already exists",
                    package.Id,
                    package.Version);

                return IndexingResult.PackageAlreadyExists;
            }
        }

        private Package GetPakageMetadata(PackageArchiveReader packageReader)
        {
            var nuspec = packageReader.NuspecReader;

            return new Package
            {
                Id = nuspec.GetId(),
                Version = nuspec.GetVersion(),
                Authors = nuspec.GetAuthors(),
                Description = nuspec.GetDescription(),
                Language = nuspec.GetLanguage() ?? string.Empty,
                Listed = true,
                MinClientVersion = nuspec.GetMinClientVersion()?.ToNormalizedString() ?? string.Empty,
                Published = DateTime.UtcNow,
                RequireLicenseAcceptance = nuspec.GetRequireLicenseAcceptance(),
                Summary = nuspec.GetSummary(),
                Title = nuspec.GetTitle(),
                IconUrl = ParseUri(nuspec.GetIconUrl()),
                LicenseUrl = ParseUri(nuspec.GetLicenseUrl()),
                ProjectUrl = ParseUri(nuspec.GetProjectUrl()),
                Dependencies = nuspec
                    .GetDependencyGroups()
                    .Select(group => new BagetPackageDependencyGroup
                    {
                        TargetFramework = group.TargetFramework.DotNetFrameworkName,
                        Dependencies = group.Packages
                            .Select(p => new BaGetPackageDependency
                            {
                                Id = p.Id,
                                VersionRange = p.VersionRange.OriginalString
                            })
                            .ToList()
                    })
                    .ToList(),
                Tags = ParseTags(nuspec.GetTags())
            };
        }

        private Uri ParseUri(string uriString)
        {
            if (string.IsNullOrEmpty(uriString)) return null;

            return new Uri(uriString);
        }

        private string[] ParseTags(string tags)
        {
            if (string.IsNullOrEmpty(tags)) return new string[0];

            return tags.Split(',', ';', '\t', ' ');
        }
    }
}