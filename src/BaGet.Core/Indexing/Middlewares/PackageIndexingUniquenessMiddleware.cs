using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    /// <summary>
    /// Validates that the new package doesn't already exist.
    /// This step should run before the package is saved to storage, database, or search.
    /// </summary>
    public class PackageIndexingUniquenessMiddleware : IPackageIndexingMiddleware
    {
        private readonly IPackageService _packages;
        private readonly IPackageStorageService _storage;
        private readonly IOptionsSnapshot<BaGetOptions> _options;
        private readonly ILogger<PackageIndexingUniquenessMiddleware> _logger;

        public PackageIndexingUniquenessMiddleware(
            IPackageService packages,
            IPackageStorageService storage,
            IOptionsSnapshot<BaGetOptions> options,
            ILogger<PackageIndexingUniquenessMiddleware> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PackageIndexingResult> IndexAsync(PackageIndexingContext context, PackageIndexingDelegate next)
        {
            // Ensure this is a new package.
            if (await _packages.ExistsAsync(context.Package.Id, context.Package.Version, context.CancellationToken))
            {
                if (!_options.Value.AllowPackageOverwrites)
                {
                    _logger.LogWarning(
                        "Failed to index package {Id} {Version} as it already exists and overwrites are disabled.",
                        context.Package.Id,
                        context.Package.NormalizedVersionString);

                    return new PackageIndexingResult(
                        PackageIndexingStatus.PackageAlreadyExists,
                        "Enable package overwrites to replace previously uploaded packages.");
                }

                _logger.LogInformation(
                    "Package {Id} {Version} already exists. Deleting the package...",
                    context.Package.Id,
                    context.Package.NormalizedVersionString);

                await _packages.HardDeletePackageAsync(
                    context.Package.Id,
                    context.Package.Version,
                    context.CancellationToken);

                await _storage.DeleteAsync(
                    context.Package.Id,
                    context.Package.Version,
                    context.CancellationToken);
            }

            return await next();
        }
    }
}
