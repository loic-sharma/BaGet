using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    /// <summary>
    /// Saves the package's metadata to the database.
    /// </summary>
    /// <remarks>
    /// This step should run after the package has been saved to storage,
    /// but before the package is saved to the search index.
    /// </remarks>
    public class PackageIndexingMetadataMiddleware : IPackageIndexingMiddleware
    {
        private readonly IPackageService _packages;
        private readonly ILogger<PackageIndexingMetadataMiddleware> _logger;

        public PackageIndexingMetadataMiddleware(
            IPackageService packages,
            ILogger<PackageIndexingMetadataMiddleware> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PackageIndexingResult> IndexAsync(PackageIndexingContext context, PackageIndexingDelegate next)
        {
            _logger.LogInformation(
                "Saving package {Id} {Version} metadata to database...",
                context.Package.Id,
                context.Package.NormalizedVersionString);

            var result = await _packages.AddAsync(context.Package, context.CancellationToken);
            if (result == PackageAddResult.PackageAlreadyExists)
            {
                _logger.LogWarning(
                    "Package {Id} {Version} metadata already exists in database.",
                    context.Package.Id,
                    context.Package.NormalizedVersionString);

                return new PackageIndexingResult(PackageIndexingStatus.PackageAlreadyExists);
            }

            if (result != PackageAddResult.Success)
            {
                _logger.LogError(
                    "Adding package {Id] {Version} metadata to database resulted in " +
                    $"unknown {nameof(PackageAddResult)} value: {{PackageAddResult}}.",
                    context.Package.Id,
                    context.Package.NormalizedVersionString,
                    result);

                return new PackageIndexingResult(PackageIndexingStatus.UnexpectedError);
            }

            _logger.LogInformation(
                "Successfully saved package {Id} {Version} metadata to database.",
                context.Package.Id,
                context.Package.NormalizedVersionString);

            return await next();
        }
    }
}
