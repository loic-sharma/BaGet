using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    /// <summary>
    /// Saves the package content to storage.
    /// </summary>
    /// <remarks>
    /// This step should run after the package has been validated, but before
    /// the package has been saved to the database and the search index.
    /// </remarks>
    public class PackageIndexingStorageMiddleware : IPackageIndexingMiddleware
    {
        private readonly IPackageStorageService _storage;
        private readonly ILogger<PackageIndexingStorageMiddleware> _logger;

        public PackageIndexingStorageMiddleware(
            IPackageStorageService storage,
            ILogger<PackageIndexingStorageMiddleware> logger)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PackageIndexingResult> IndexAsync(PackageIndexingContext context, PackageIndexingDelegate next)
        {
            _logger.LogInformation(
                "Saving package {PackageId} {PackageVersion} content to storage...",
                context.Package.Id,
                context.Package.NormalizedVersionString);

            try
            {
                // Rewind all streams before saving content..
                context.PackageStream.Position = 0;
                context.NuspecStream.Position = 0;

                if (context.ReadmeStream != null) context.ReadmeStream.Position = 0;
                if (context.IconStream != null) context.IconStream.Position = 0;

                await _storage.SavePackageContentAsync(
                    context.Package,
                    context.PackageStream,
                    context.NuspecStream,
                    context.ReadmeStream,
                    context.IconStream,
                    context.CancellationToken);
            }
            catch (Exception e)
            {
                // This may happen due to concurrent pushes.
                // TODO: Make IPackageStorageService.SavePackageContentAsync return a result enum so this
                // can be properly handled.
                _logger.LogError(
                    e,
                    "Failed to save package {PackageId} {PackageVersion} to storage.",
                    context.Package.Id,
                    context.Package.NormalizedVersionString);

                return new PackageIndexingResult(
                    PackageIndexingStatus.UnexpectedError,
                    "Failed to save package to storage due to unexpected exception");
            }

            _logger.LogInformation(
                "Successfully savead package {Id} {Version} to storage.",
                context.Package.Id,
                context.Package.NormalizedVersionString);

            return await next();
        }
    }
}
