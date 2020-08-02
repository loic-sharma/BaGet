using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    public class PackageMetadataMiddleware : IPackageIndexingMiddleware
    {
        private readonly IPackageService _packages;
        private readonly ILogger<PackageMetadataMiddleware> _logger;

        public PackageMetadataMiddleware(
            IPackageService packages,
            ILogger<PackageMetadataMiddleware> logger)
        {
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task IndexAsync(PackageIndexingContext context, PackageIndexingDelegate next)
        {
            _logger.LogInformation(
                "Persisted package {Id} {Version} content to storage, saving metadata to database...",
                context.Package.Id,
                context.Package.NormalizedVersionString);

            var result = await _packages.AddAsync(context.Package, context.CancellationToken);
            if (result == PackageAddResult.PackageAlreadyExists)
            {
                _logger.LogWarning(
                    "Package {Id} {Version} metadata already exists in database.",
                    context.Package.Id,
                    context.Package.NormalizedVersionString);

                context.Status = PackageIndexingStatus.PackageAlreadyExists;
                return;
            }

            if (result != PackageAddResult.Success)
            {
                _logger.LogError(
                    "Adding package {Id] {Version} metadata to database resulted in " +
                    $"unknown {nameof(PackageAddResult)} value: {{PackageAddResult}}.",
                    context.Package.Id,
                    context.Package.NormalizedVersionString,
                    result);

                context.Status = PackageIndexingStatus.UnexpectedError;
                return;
            }

            _logger.LogInformation(
                "Successfully persisted package {Id} {Version} metadata to database.",
                context.Package.Id,
                context.Package.NormalizedVersionString);

            await next();
        }
    }
}
