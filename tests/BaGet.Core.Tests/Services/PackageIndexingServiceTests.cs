using System;
using Microsoft.Extensions.Logging;
using Moq;

namespace BaGet.Core.Tests.Services
{
    public class PackageIndexingServiceTests
    {
        private readonly Mock<IServiceProvider> _services;
        private readonly Mock<SystemTime> _time;
        private readonly PackageIndexingService _target;

        public PackageIndexingServiceTests()
        {
            _services = new Mock<IServiceProvider>();
            _time = new Mock<SystemTime>();

            _target = new PackageIndexingService(
                _services.Object,
                _time.Object,
                Mock.Of<ILogger<PackageIndexingService>>());
        }

        // Check that context is properly created.

        // Check that middlewares from service provider are used.
        // Check that middlewares are called in order.
        // Check that the streams are rewinded.
    }
}
