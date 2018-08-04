using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Extensions;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;

namespace BaGet.Core.Services
{
    using BaGetPackageDependency = Entities.PackageDependency;

    public class IndexingService : IIndexingService
    {
        private readonly IPackageService _packages;
        private readonly IPackageStorageService _storage;
        private readonly ISearchService _search;
        private readonly ILogger<IndexingService> _logger;

        public IndexingService(
            IPackageService packages,
            IPackageStorageService storage,
            ISearchService search,
            ILogger<IndexingService> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _search = search ?? throw new ArgumentNullException(nameof(search));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IndexingResult> IndexAsync(Stream stream)
        {
            // Try to save the package stream to storage.
            // TODO: On exception, roll back storage save.
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
                        _logger.LogInformation(
                            "Persisting package {Id} {Version} content to storage...",
                            packageId,
                            packageVersion.ToNormalizedString());

                        await _storage.SavePackageStreamAsync(packageReader, stream);
                    }
                    catch (Exception e)
                    {
                        // This may happen due to concurrent pushes.
                        // TODO: Make IStorageService.SaveAsync return a result enum so this can be properly handled.
                        _logger.LogError(e, "Failed to save package {Id} {Version}", packageId, packageVersion.ToNormalizedString());

                        throw;
                    }

                    try
                    {
                        package = GetPackageMetadata(packageReader);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(
                            e,
                            "Failed to extract metadata for package {Id} {Version}",
                            packageId,
                            packageVersion.ToNormalizedString());

                        throw;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Uploaded package is invalid or the package already existed in storage");

                return IndexingResult.InvalidPackage;
            }

            // The package stream has been stored. Persist the package's metadata to the database.
            _logger.LogInformation(
                "Persisting package {Id} {Version} metadata to database...",
                package.Id,
                package.VersionString);

            var result = await _packages.AddAsync(package);

            switch (result)
            {
                case PackageAddResult.Success:
                    _logger.LogInformation(
                        "Successfully persisted package {Id} {Version} metadata to database. Indexing in search...",
                        package.Id,
                        package.VersionString);

                    await _search.IndexAsync(package);

                    _logger.LogInformation(
                        "Successfully indexed package {Id} {Version} in search",
                        package.Id,
                        package.VersionString);

                    return IndexingResult.Success;

                case PackageAddResult.PackageAlreadyExists:
                    _logger.LogWarning(
                        "Package {Id} {Version} metadata already exists in database",
                        package.Id,
                        package.VersionString);

                    return IndexingResult.PackageAlreadyExists;

                default:
                    _logger.LogError($"Unknown {nameof(PackageAddResult)} value: {{PackageAddResult}}", result);

                    throw new InvalidOperationException($"Unknown {nameof(PackageAddResult)} value: {result}");
            }
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
                RepositoryUrl = repositoryUri,
                RepositoryType = repositoryType,
                Dependencies = GetDependencies(nuspec),
                Tags = ParseTags(nuspec.GetTags())
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
                        Id = null,
                        VersionRange = null,
                        TargetFramework = targetFramework,
                    });
                }

                foreach (var dependency in group.Packages)
                {
                    dependencies.Add(new BaGetPackageDependency
                    {
                        Id = dependency.Id,
                        VersionRange = dependency.VersionRange?.ToString(),
                        TargetFramework = targetFramework,
                    });
                }
            }

            return dependencies;
        }
    }
}