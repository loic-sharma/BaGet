using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    /// <summary>
    /// Saves the package to the search index.
    /// </summary>
    /// <remarks>
    /// This step should run after the package has been saved to storage and the database.
    /// </remarks>
    public class PackageIndexingSearchMiddleware : IPackageIndexingMiddleware
    {
        private readonly ISearchIndexer _search;
        private readonly ILogger<PackageIndexingSearchMiddleware> _logger;

        public PackageIndexingSearchMiddleware(
            ISearchIndexer search,
            ILogger<PackageIndexingSearchMiddleware> logger)
        {
            _search = search ?? throw new ArgumentNullException(nameof(search));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PackageIndexingResult> IndexAsync(PackageIndexingContext context, PackageIndexingDelegate next)
        {
            _logger.LogInformation(
                "Adding package {Id} {Version} to the search index...",
                context.Package.Id,
                context.Package.NormalizedVersionString);

            await _search.IndexAsync(context.Package, context.CancellationToken);

            _logger.LogInformation(
                "Successfully added package {Id} {Version} to the search index.",
                context.Package.Id,
                context.Package.NormalizedVersionString);

            return await next();
        }
    }
}
