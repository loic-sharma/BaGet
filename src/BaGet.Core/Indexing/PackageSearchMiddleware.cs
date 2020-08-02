using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    /// <summary>
    /// Indexes a package to the search service.
    /// </summary>
    public class PackageSearchMiddleware : IPackageIndexingMiddleware
    {
        private readonly ISearchIndexer _search;
        private readonly ILogger<PackageSearchMiddleware> _logger;

        public PackageSearchMiddleware(
            ISearchIndexer search,
            ILogger<PackageSearchMiddleware> logger)
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
