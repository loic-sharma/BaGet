using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;

namespace BaGet.Core
{
    public class PackageIndexingService : IPackageIndexingService
    {
        private readonly IServiceProvider _services;
        private readonly SystemTime _time;
        private readonly ILogger<PackageIndexingService> _logger;

        public PackageIndexingService(
            IServiceProvider services,
            SystemTime time,
            ILogger<PackageIndexingService> logger)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PackageIndexingResult> IndexAsync(Stream packageStream, CancellationToken cancellationToken)
        {
            using var context = await CreateContextOrNullAsync(packageStream, cancellationToken);
            if (context == null)
            {
                return new PackageIndexingResult(PackageIndexingStatus.InvalidPackage);
            }

            return await CreateIndexer(context).Invoke();
        }

        private async Task<PackageIndexingContext> CreateContextOrNullAsync(
            Stream packageStream,
            CancellationToken cancellationToken)
        {
            // Try to extract all the necessary information from the package.
            Package package;
            Stream nuspecStream = null;
            Stream readmeStream = null;
            Stream iconStream = null;

            try
            {
                using var packageReader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);

                package = packageReader.GetPackageMetadata();
                package.Published = _time.UtcNow;

                nuspecStream = await packageReader.GetNuspecAsync(cancellationToken);
                nuspecStream = await nuspecStream.AsTemporaryFileStreamAsync(cancellationToken);

                if (package.HasReadme)
                {
                    readmeStream = await packageReader.GetReadmeAsync(cancellationToken);
                    readmeStream = await readmeStream.AsTemporaryFileStreamAsync(cancellationToken);
                }
                else
                {
                    readmeStream = null;
                }

                if (package.HasEmbeddedIcon)
                {
                    iconStream = await packageReader.GetIconAsync(cancellationToken);
                    iconStream = await iconStream.AsTemporaryFileStreamAsync(cancellationToken);
                }
                else
                {
                    iconStream = null;
                }

                return new PackageIndexingContext
                {
                    Package = package,
                    PackageStream = packageStream,
                    NuspecStream = nuspecStream,
                    IconStream = iconStream,
                    ReadmeStream = readmeStream,
                    CancellationToken = cancellationToken,
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Uploaded package is invalid");

                packageStream?.Dispose();
                nuspecStream?.Dispose();
                iconStream?.Dispose();
                readmeStream?.Dispose();

                return null;
            }
        }

        private PackageIndexingDelegate CreateIndexer(PackageIndexingContext context)
        {
            // Create the "terminal" indexer that runs at the final step in the indexing pipeline.
            // If we reach the end of the pipeline, the package has been indexed successfully and
            // we can return a successful status code.
            PackageIndexingDelegate indexer = () =>
            {
                var result = new PackageIndexingResult(PackageIndexingStatus.Success);
                return Task.FromResult(result);
            };

            // Now add each indexer middleware to the pipeline.
            var middlewares = _services.GetRequiredService<IEnumerable<IPackageIndexingMiddleware>>();

            foreach (var middleware in middlewares.Reverse())
            {
                var next = indexer;

                indexer = () => middleware.IndexAsync(context, next);
            }

            return indexer;
        }
    }
}
