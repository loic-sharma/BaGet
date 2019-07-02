using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Configuration;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using BaGet.Core.Search;
using BaGet.Core.State;
using BaGet.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;

namespace BaGet.Core.Indexing
{
    using NuGetPackageType = NuGet.Packaging.Core.PackageType;

    public class PackageIndexingService : IPackageIndexingService
    {
        private readonly IPackageService _packages;
        private readonly IPackageStorageService _storage;
        private readonly ISearchService _search;
        private readonly IOptionsSnapshot<BaGetOptions> _options;
        private readonly ILogger<PackageIndexingService> _logger;

        public PackageIndexingService(
            IPackageService packages,
            IPackageStorageService storage,
            ISearchService search,
            IOptionsSnapshot<BaGetOptions> options,
            ILogger<PackageIndexingService> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _search = search ?? throw new ArgumentNullException(nameof(search));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PackageIndexingResult> IndexAsync(Stream packageStream, CancellationToken cancellationToken)
        {
            // Try to extract all the necessary information from the package.
            Package package;
            Stream nuspecStream;
            Stream readmeStream;

            try
            {
                using (var packageReader = new PackageArchiveReader(packageStream, leaveStreamOpen: true))
                {
                    package = GetPackageMetadata(packageReader);
                    nuspecStream = await packageReader.GetNuspecAsync(cancellationToken);
                    nuspecStream = await nuspecStream.AsTemporaryFileStreamAsync();

                    if (package.HasReadme)
                    {
                        readmeStream = await packageReader.GetReadmeAsync(cancellationToken);
                        readmeStream = await readmeStream.AsTemporaryFileStreamAsync();
                    }
                    else
                    {
                        readmeStream = null;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Uploaded package is invalid");

                return PackageIndexingResult.InvalidPackage;
            }

            // The package is well-formed. Ensure this is a new package.
            if (await _packages.ExistsAsync(package.Id, package.Version))
            {
                if (!_options.Value.AllowPackageOverwrites || !new Regex(_options.Value.OverwriteMatch).IsMatch($"{package.Id} {package.VersionString}"))
                {
                    return PackageIndexingResult.PackageAlreadyExists;
                }

                await _packages.HardDeletePackageAsync(package.Id, package.Version);
                await _storage.DeleteAsync(package.Id, package.Version, cancellationToken);
            }

            // TODO: Add more package validations
            // TODO: Call PackageArchiveReader.ValidatePackageEntriesAsync
            _logger.LogInformation(
                "Validated package {PackageId} {PackageVersion}, persisting content to storage...",
                package.Id,
                package.VersionString);

            try
            {
                packageStream.Position = 0;

                await _storage.SavePackageContentAsync(
                    package,
                    packageStream,
                    nuspecStream,
                    readmeStream,
                    cancellationToken);
            }
            catch (Exception e)
            {
                // This may happen due to concurrent pushes.
                // TODO: Make IPackageStorageService.SavePackageContentAsync return a result enum so this
                // can be properly handled.
                _logger.LogError(
                    e,
                    "Failed to persist package {PackageId} {PackageVersion} content to storage",
                    package.Id,
                    package.VersionString);

                throw;
            }

            _logger.LogInformation(
                "Persisted package {Id} {Version} content to storage, saving metadata to database...",
                package.Id,
                package.VersionString);

            var result = await _packages.AddAsync(package);
            if (result == PackageAddResult.PackageAlreadyExists)
            {
                _logger.LogWarning(
                    "Package {Id} {Version} metadata already exists in database",
                    package.Id,
                    package.VersionString);

                return PackageIndexingResult.PackageAlreadyExists;
            }

            if (result != PackageAddResult.Success)
            {
                _logger.LogError($"Unknown {nameof(PackageAddResult)} value: {{PackageAddResult}}", result);

                throw new InvalidOperationException($"Unknown {nameof(PackageAddResult)} value: {result}");
            }

            _logger.LogInformation(
                "Successfully persisted package {Id} {Version} metadata to database. Indexing in search...",
                package.Id,
                package.VersionString);

            await _search.IndexAsync(package);

            _logger.LogInformation(
                "Successfully indexed package {Id} {Version} in search",
                package.Id,
                package.VersionString);

            return PackageIndexingResult.Success;
        }

        private Package GetPackageMetadata(PackageArchiveReader packageReader)
        {
            var nuspec = packageReader.NuspecReader;

            (var repositoryUri, var repositoryType) = GetRepositoryMetadata(nuspec);

            return new Package
            {
                Id = nuspec.GetId(),
                Version = nuspec.GetVersion(),
                Authors = ParseAuthors(nuspec.GetAuthors()),
                Description = nuspec.GetDescription(),
                HasReadme = packageReader.HasReadme(),
                IsPrerelease = nuspec.GetVersion().IsPrerelease,
                Language = nuspec.GetLanguage() ?? string.Empty,
                Listed = true,
                MinClientVersion = nuspec.GetMinClientVersion()?.ToNormalizedString() ?? string.Empty,
                Published = DateTime.UtcNow,
                RequireLicenseAcceptance = nuspec.GetRequireLicenseAcceptance(),
                SemVerLevel = GetSemVerLevel(nuspec),
                Summary = nuspec.GetSummary(),
                Title = nuspec.GetTitle(),
                IconUrl = ParseUri(nuspec.GetIconUrl()),
                LicenseUrl = ParseUri(nuspec.GetLicenseUrl()),
                ProjectUrl = ParseUri(nuspec.GetProjectUrl()),
                RepositoryUrl = repositoryUri,
                RepositoryType = repositoryType,
                Dependencies = GetDependencies(nuspec),
                Tags = ParseTags(nuspec.GetTags()),
                PackageTypes = GetPackageTypes(nuspec),
                TargetFrameworks = GetTargetFrameworks(packageReader),
            };
        }

        // Based off https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/SemVerLevelKey.cs
        private SemVerLevel GetSemVerLevel(NuspecReader nuspec)
        {
            if (nuspec.GetVersion().IsSemVer2)
            {
                return SemVerLevel.SemVer2;
            }

            foreach (var dependencyGroup in nuspec.GetDependencyGroups())
            {
                foreach (var dependency in dependencyGroup.Packages)
                {
                    if ((dependency.VersionRange.MinVersion != null && dependency.VersionRange.MinVersion.IsSemVer2)
                        || (dependency.VersionRange.MaxVersion != null && dependency.VersionRange.MaxVersion.IsSemVer2))
                    {
                        return SemVerLevel.SemVer2;
                    }
                }
            }

            return SemVerLevel.Unknown;
        }

        private Uri ParseUri(string uriString)
        {
            if (string.IsNullOrEmpty(uriString)) return null;

            return new Uri(uriString);
        }

        private string[] ParseAuthors(string authors)
        {
            if (string.IsNullOrEmpty(authors)) return new string[0];

            return authors.Split(new[] { ',', ';', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private string[] ParseTags(string tags)
        {
            if (string.IsNullOrEmpty(tags)) return new string[0];

            return tags.Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private (Uri repositoryUrl, string repositoryType) GetRepositoryMetadata(NuspecReader nuspec)
        {
            var repository = nuspec.GetRepositoryMetadata();

            if (string.IsNullOrEmpty(repository?.Url) ||
                !Uri.TryCreate(repository.Url, UriKind.Absolute, out var repositoryUri))
            {
                return (null, null);
            }

            if (repositoryUri.Scheme != Uri.UriSchemeHttps)
            {
                return (null, null);
            }

            if (repository.Type.Length > 100)
            {
                throw new InvalidOperationException("Repository type must be less than or equal 100 characters");
            }

            return (repositoryUri, repository.Type);
        }

        private List<PackageDependency> GetDependencies(NuspecReader nuspec)
        {
            var dependencies = new List<PackageDependency>();

            foreach (var group in nuspec.GetDependencyGroups())
            {
                var targetFramework = group.TargetFramework.GetShortFolderName();

                if (!group.Packages.Any())
                {
                    dependencies.Add(new PackageDependency
                    {
                        Id = null,
                        VersionRange = null,
                        TargetFramework = targetFramework,
                    });
                }

                foreach (var dependency in group.Packages)
                {
                    dependencies.Add(new PackageDependency
                    {
                        Id = dependency.Id,
                        VersionRange = dependency.VersionRange?.ToString(),
                        TargetFramework = targetFramework,
                    });
                }
            }

            return dependencies;
        }

        private List<PackageType> GetPackageTypes(NuspecReader nuspec)
        {
            var packageTypes = nuspec
                .GetPackageTypes()
                .Select(t => new PackageType
                {
                    Name = t.Name,
                    Version = t.Version.ToString()
                })
                .ToList();

            // Default to the standard "dependency" package type if no types were found.
            if (packageTypes.Count == 0)
            {
                packageTypes.Add(new PackageType
                {
                    Name = NuGetPackageType.Dependency.Name,
                    Version = NuGetPackageType.Dependency.Version.ToString(),
                });
            }

            return packageTypes;
        }

        private List<TargetFramework> GetTargetFrameworks(PackageArchiveReader packageReader)
        {
            var targetFrameworks = packageReader
                .GetSupportedFrameworks()
                .Select(f => new TargetFramework
                {
                    Moniker = f.GetShortFolderName()
                })
                .ToList();

            // Default to the "any" framework if no frameworks were found.
            if (targetFrameworks.Count == 0)
            {
                targetFrameworks.Add(new TargetFramework { Moniker = "any" });
            }

            return targetFrameworks;
        }
    }
}
