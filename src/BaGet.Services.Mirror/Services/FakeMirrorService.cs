using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System;
using System.Threading.Tasks;

namespace BaGet.Services.Mirror
{
    public class FakeMirrorService : IMirrorService
    {

        private readonly ILogger<MirrorService> _logger;

        public FakeMirrorService(
            ILogger<MirrorService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        public Task MirrorAsync(string id, NuGetVersion version) => Task.CompletedTask;
    }
}
