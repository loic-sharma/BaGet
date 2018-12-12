using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;

namespace BaGet.Core.Services
{
    using BaGetPackageDependency = Entities.PackageDependency;

    public class PackageIndexingService : IPackageIndexingService
    {
        private readonly IPackageService _packages;
        private readonly IPackageStorageService _storage;
        private readonly ISearchService _search;
        private readonly ILogger<PackageIndexingService> _logger;

        public PackageIndexingService(
            IPackageService packages,
            IPackageStorageService storage,
            ISearchService search,
            ILogger<PackageIndexingService> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _search = search ?? throw new ArgumentNullException(nameof(search));
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

                    if (package.HasReadme)
                    {
                        readmeStream = await packageReader.GetReadmeAsync(cancellationToken);
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
            if (await _packages.ExistsAsync(package.PackageId, package.Version))
            {
                return PackageIndexingResult.PackageAlreadyExists;
            }

            // TODO: Add more package validations
            // TODO: Call PackageArchiveReader.ValidatePackageEntriesAsync
            _logger.LogInformation(
                "Validated package {PackageId} {PackageVersion}, persisting content to storage...",
                package.PackageId,
                package.Version);

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
                    package.PackageId,
                    package.Version);

                throw;
            }

            _logger.LogInformation(
                "Persisted package {Id} {Version} content to storage, saving metadata to database...",
                package.PackageId,
                package.Version);

            var result = await _packages.AddAsync(package);
            if (result == PackageAddResult.PackageAlreadyExists)
            {
                _logger.LogWarning(
                    "Package {Id} {Version} metadata already exists in database",
                    package.PackageId,
                    package.Version);

                return PackageIndexingResult.PackageAlreadyExists;
            }

            if (result != PackageAddResult.Success)
            {
                _logger.LogError($"Unknown {nameof(PackageAddResult)} value: {{PackageAddResult}}", result);

                throw new InvalidOperationException($"Unknown {nameof(PackageAddResult)} value: {result}");
            }

            _logger.LogInformation(
                "Successfully persisted package {Id} {Version} metadata to database. Indexing in search...",
                package.PackageId,
                package.Version);

            await _search.IndexAsync(package);

            _logger.LogInformation(
                "Successfully indexed package {Id} {Version} in search",
                package.PackageId,
                package.Version);

            return PackageIndexingResult.Success;
        }

        private Package GetPackageMetadata(PackageArchiveReader packageReader)
        {
            var nuspec = packageReader.NuspecReader;

            (var repositoryUri, var repositoryType) = GetRepositoryMetadata(nuspec);

            return new Package
            {
                PackageId = nuspec.GetId(),
                Version = nuspec.GetVersion().ToNormalizedString(),
                Authors = ParseAuthors(nuspec.GetAuthors()),
                Description = nuspec.GetDescription(),
                HasReadme = packageReader.HasReadme(),
                Language = nuspec.GetLanguage() ?? string.Empty,
                Listed = true,
                MinClientVersion = nuspec.GetMinClientVersion()?.ToNormalizedString() ?? string.Empty,
                Published = DateTime.UtcNow,
                RequireLicenseAcceptance = nuspec.GetRequireLicenseAcceptance(),
                Summary = nuspec.GetSummary(),
                Title = nuspec.GetTitle(),
                IconUrl = nuspec.GetIconUrl(),
                LicenseUrl = nuspec.GetLicenseUrl(),
                ProjectUrl = nuspec.GetProjectUrl(),
                RepositoryUrl = repositoryUri.ToString(),
                RepositoryType = repositoryType,
                Dependencies = GetDependencies(nuspec),
                Tags = ParseTags(nuspec.GetTags()).Select(s=>new PackageTag(s)).ToArray()
            };
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

        private List<BaGetPackageDependency> GetDependencies(NuspecReader nuspec)
        {
            var dependencies = new List<BaGetPackageDependency>();

            foreach (var group in nuspec.GetDependencyGroups())
            {
                var targetFramework = group.TargetFramework.GetShortFolderName();

                if (!group.Packages.Any())
                {
                    dependencies.Add(new BaGetPackageDependency
                    {
                        PackageId = null,
                        VersionRange = null,
                        TargetFramework = targetFramework,
                    });
                }

                foreach (var dependency in group.Packages)
                {
                    dependencies.Add(new BaGetPackageDependency
                    {
                        PackageId = dependency.Id,
                        VersionRange = dependency.VersionRange?.ToString(),
                        TargetFramework = targetFramework,
                    });
                }
            }

            return dependencies;
        }
    }
}
