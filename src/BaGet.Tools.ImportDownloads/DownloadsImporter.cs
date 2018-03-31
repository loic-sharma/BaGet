using System;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoreLinq;

namespace BaGet.Tools.ImportDownloads
{
    public class DownloadsImporter
    {
        private const int BatchSize = 1000;

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

        public async Task ImportAsync()
        {
            var packageDownloads = await _downloadsSource.GetPackageDownloadsAsync();
            var packages = await _context.Packages.ToListAsync();

            var batchCount = 1;

            foreach (var batch in packages.Batch(BatchSize))
            {
                _logger.LogInformation("Importing batch {BatchCount}...", batchCount);

                foreach (var package in packages)
                {
                    var packageId = package.Id.ToLowerInvariant();
                    var packageVersion = package.VersionString.ToLowerInvariant();

                    if (!packageDownloads.ContainsKey(packageId) ||
                        !packageDownloads[packageId].ContainsKey(packageVersion))
                    {
                        continue;
                    }

                    package.Downloads = packageDownloads[packageId][packageVersion];
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Imported batch {BatchCount}", batchCount);

                batchCount++;
            }
        }
    }
}
