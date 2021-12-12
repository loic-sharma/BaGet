using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    public class DownloadsImporter
    {
        private const int BatchSize = 200;

        private readonly IContext _context;
        private readonly IPackageDownloadsSource _downloadsSource;
        private readonly ILogger<DownloadsImporter> _logger;

        public DownloadsImporter(
            IContext context,
            IPackageDownloadsSource downloadsSource,
            ILogger<DownloadsImporter> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _downloadsSource = downloadsSource ?? throw new ArgumentNullException(nameof(downloadsSource));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ImportAsync(CancellationToken cancellationToken)
        {
            var packageDownloads = await _downloadsSource.GetPackageDownloadsAsync();
            var packages = await _context.Packages.CountAsync();
            var batches = (packages / BatchSize) + 1;

            for (var batch = 0; batch < batches; batch++)
            {
                _logger.LogInformation("Importing batch {Batch}...", batch);

                foreach (var package in await GetBatchAsync(batch, cancellationToken))
                {
                    var packageId = package.Id.ToLowerInvariant();
                    var packageVersion = package.NormalizedVersionString.ToLowerInvariant();

                    if (!packageDownloads.ContainsKey(packageId) ||
                        !packageDownloads[packageId].ContainsKey(packageVersion))
                    {
                        continue;
                    }

                    package.Downloads = packageDownloads[packageId][packageVersion];
                }

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Imported batch {Batch}", batch);
            }
        }

        private Task<List<Package>> GetBatchAsync(int batch, CancellationToken cancellationToken)
            => _context.Packages
                .OrderBy(p => p.Key)
                .Skip(batch * BatchSize)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
    }
}
