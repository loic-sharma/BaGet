using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Extensions.Logging;

namespace BaGet.Azure.Search
{
    public class BatchIndexer
    {
        public const int MaxBatchSize = 1000;

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
                var document = await BuildDocumentAsync(packageId);

                actions.Add(IndexAction.Upload(document));
            }

            var batch = IndexBatch.New(actions);

            // TODO: Add retry on IndexBatchException
            // See: https://docs.microsoft.com/en-us/azure/search/search-import-data-dotnet#import-data-to-the-index
            await _indexClient.Documents.IndexAsync(batch);

            _logger.LogInformation("Indexed {PackageCount} packages", packageIdSet.Count);
        }

        private async Task<PackageDocument> BuildDocumentAsync(string packageId)
        {
            if (packageId == null) throw new ArgumentNullException(nameof(packageId));

            var packages = await _packageService.FindAsync(packageId);

            if (packages.Count == 0)
            {
                _logger.LogError("Could not find package with id {PackageId}", packageId);

                throw new ArgumentException($"Invalid package id {packageId}", nameof(packageId));
            }

            var result = new PackageDocument();

            var latest = packages.OrderByDescending(p => p.Version).First();
            var versions = packages.OrderBy(p => p.Version).ToList();
            var dependencies = latest
                .Dependencies
                .Select(d => d.Id?.ToLowerInvariant())
                .Where(d => d != null)
                .Distinct()
                .ToArray();

            result.Key = EncodeKey(packageId.ToLowerInvariant());
            result.Id = latest.Id;
            result.Version = latest.VersionString;
            result.Description = latest.Description;
            result.Authors = latest.Authors;
            result.IconUrl = latest.IconUrlString;
            result.LicenseUrl = latest.LicenseUrlString;
            result.ProjectUrl = latest.ProjectUrlString;
            result.Published = latest.Published;
            result.Summary = latest.Summary;
            result.Tags = latest.Tags;
            result.Title = latest.Title;
            result.TotalDownloads = versions.Sum(p => p.Downloads);
            result.DownloadsMagnitude = result.TotalDownloads.ToString().Length;
            result.Versions = versions.Select(p => p.VersionString).ToArray();
            result.VersionDownloads = versions.Select(p => p.Downloads.ToString()).ToArray();
            result.Dependencies = dependencies;

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
