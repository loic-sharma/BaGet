using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    public class PackageStorageMiddleware : IPackageIndexingMiddleware
    {
        private readonly IPackageStorageService _storage;
        private readonly ILogger<PackageStorageMiddleware> _logger;

        public PackageStorageMiddleware(
            IPackageStorageService storage,
            ILogger<PackageStorageMiddleware> logger)
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
                    "Failed to save package {PackageId} {PackageVersion} content to storage",
                    context.Package.Id,
                    context.Package.NormalizedVersionString);

                return new PackageIndexingResult(
                    PackageIndexingStatus.UnexpectedError,
                    "Failed to save package to storage due to unexpected exception");
            }

            return await next();
        }
    }
}
