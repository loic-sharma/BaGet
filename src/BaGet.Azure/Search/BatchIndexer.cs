using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Extensions.Logging;

namespace BaGet.Azure.Search
{
    public class BatchIndexer
    {
        /// <summary>
        /// Each package creates up to 4 documents, Azure Search accepts batches of up to 1000 documents.
        /// </summary>
        public const int MaxBatchSize = 1000 / 4;

        private readonly IPackageService _packageService;
        private readonly ISearchIndexClient _indexClient;
        private readonly ILogger<BatchIndexer> _logger;

        public BatchIndexer(
            IPackageService packageService,
            SearchServiceClient searchClient,
            ILogger<BatchIndexer> logger)
        {
            if (searchClient == null) throw new ArgumentNullException(nameof(searchClient));

            _indexClient = searchClient.Indexes.GetClient(PackageDocument.IndexName);

            _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task IndexAsync(params string[] packageIds)
        {
            if (packageIds == null) throw new ArgumentNullException(nameof(packageIds));

            var actions = new List<IndexAction<PackageDocument>>();
            var packageIdSet = new HashSet<string>(packageIds, StringComparer.OrdinalIgnoreCase);

            if (packageIdSet.Count > MaxBatchSize)
            {
                throw new ArgumentException($"Cannot index more than {MaxBatchSize} packages at once");
            }

            _logger.LogInformation("Indexing {PackageCount} packages...", packageIdSet.Count);

            foreach (var packageId in packageIdSet)
            {
                foreach (var document in await BuildDocumentsAsync(packageId))
                {
                    actions.Add(IndexAction.Upload(document));
                }
            }

            var batch = IndexBatch.New(actions);

            // TODO: Add retry on IndexBatchException
            // See: https://docs.microsoft.com/en-us/azure/search/search-import-data-dotnet#import-data-to-the-index
            await _indexClient.Documents.IndexAsync(batch);

            _logger.LogInformation("Indexed {PackageCount} packages", packageIdSet.Count);
        }

        private async Task<IReadOnlyList<PackageDocument>> BuildDocumentsAsync(string packageId)
        {
            if (packageId == null) throw new ArgumentNullException(nameof(packageId));

            var packages = await _packageService.FindAsync(packageId);

            if (packages.Count == 0)
            {
                _logger.LogError("Could not find package with id {PackageId}", packageId);

                throw new ArgumentException($"Invalid package id {packageId}", nameof(packageId));
            }

            var result = new List<PackageDocument>();
            for (var i = 0; i < 4; i++)
            {
                var includePrerelease = (i & 1) != 0;
                var includeSemVer2 = (i & 2) != 0;
                var searchFilters = (SearchFilters)i;

                IEnumerable<Package> filtered = packages;
                if (!includePrerelease)
                {
                    filtered = filtered.Where(p => !p.IsPrerelease);
                }

                if (!includeSemVer2)
                {
                    filtered = filtered.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
                }

                var versions = filtered.OrderBy(p => p.Version).ToList();
                if (versions.Count == 0)
                {
                    continue;
                }

                var latest = versions.Last();
                var dependencies = latest
                    .Dependencies
                    .Select(d => d.Id?.ToLowerInvariant())
                    .Where(d => d != null)
                    .Distinct()
                    .ToArray();

                var document = new PackageDocument();
                var encodedId = EncodeKey(packageId.ToLowerInvariant());

                document.Key = $"{encodedId}-{searchFilters}";
                document.Id = latest.Id;
                document.Version = latest.Version.ToFullString();
                document.Description = latest.Description;
                document.Authors = latest.Authors;
                document.IconUrl = latest.IconUrlString;
                document.LicenseUrl = latest.LicenseUrlString;
                document.ProjectUrl = latest.ProjectUrlString;
                document.Published = latest.Published;
                document.Summary = latest.Summary;
                document.Tags = latest.Tags;
                document.Title = latest.Title;
                document.TotalDownloads = versions.Sum(p => p.Downloads);
                document.DownloadsMagnitude = document.TotalDownloads.ToString().Length;
                document.Versions = versions.Select(p => p.Version.ToFullString()).ToArray();
                document.VersionDownloads = versions.Select(p => p.Downloads.ToString()).ToArray();
                document.Dependencies = dependencies;
                document.PackageTypes = latest.PackageTypes.Select(t => t.Name).ToArray();
                document.Frameworks = latest.TargetFrameworks.Select(f => f.Moniker.ToLowerInvariant()).ToArray();
                document.SearchFilters = searchFilters.ToString();

                result.Add(document);
            }

            return result;
        }

        private string EncodeKey(string key)
        {
            // Keys can only contain letters, digits, underscore(_), dash(-), or equal sign(=).
            var bytes = Encoding.UTF8.GetBytes(key);
            var base64 = Convert.ToBase64String(bytes);

            return base64.Replace('+', '-').Replace('/', '_');
        }
    }
}
