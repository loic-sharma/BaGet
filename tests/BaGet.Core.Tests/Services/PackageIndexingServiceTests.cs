using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Tests;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class PackageIndexingServiceTests
    {
        private readonly Mock<IServiceProvider> _services;
        private readonly Mock<SystemTime> _time;
        private readonly PackageIndexingService _target;

        private readonly CancellationToken _cancellationToken;

        public PackageIndexingServiceTests()
        {
            _services = new Mock<IServiceProvider>();
            _time = new Mock<SystemTime>();

            _target = new PackageIndexingService(
                _services.Object,
                _time.Object,
                Mock.Of<ILogger<PackageIndexingService>>());

            _cancellationToken = CancellationToken.None;
        }

        [Fact]
        public async Task IndexesDefaultPackage()
        {
            SetupMiddlewares(ValidateContext);

            var result = await _target.IndexAsync(PackageData.Default, _cancellationToken);

            Assert.Equal(PackageIndexingStatus.Success, result.Status);
            Assert.Empty(result.Messages);

            async Task<PackageIndexingResult> ValidateContext(PackageIndexingContext context, PackageIndexingDelegate next)
            {
                // Validate the context contains the expected package.
                Assert.Equal("DefaultPackage", context.Package.Id);
                Assert.Equal("1.2.3", context.Package.Version.ToNormalizedString());

                Assert.NotNull(context.PackageStream);
                Assert.NotNull(context.NuspecStream);
                Assert.Null(context.IconStream);
                Assert.Null(context.ReadmeStream);

                // The terminal middleware should return a successful status code with no messages.
                var result = await next();
                Assert.Equal(PackageIndexingStatus.Success, result.Status);
                Assert.Empty(result.Messages);
                return result;
            }
        }

        [Fact]
        public async Task MiddlewaresAreCalledInOrder()
        {
            var counter = 0;
            SetupMiddlewares(First, Second);

            var result = await _target.IndexAsync(PackageData.Default, _cancellationToken);

            Assert.Equal(4, counter);
            Assert.Equal(PackageIndexingStatus.PackageAlreadyExists, result.Status);
            Assert.Equal(2, result.Messages.Count);
            Assert.Equal("Hello from second", result.Messages[0]);
            Assert.Equal("Hello from first", result.Messages[1]);

            async Task<PackageIndexingResult> First(PackageIndexingContext context, PackageIndexingDelegate next)
            {
                Assert.Equal(0, counter);
                counter++;

                var result = await next();

                Assert.Equal(3, counter);
                Assert.Equal(PackageIndexingStatus.InvalidPackage, result.Status);

                result.Status = PackageIndexingStatus.PackageAlreadyExists;
                result.Messages.Add("Hello from first");
                counter++;

                return result;
            }

            async Task<PackageIndexingResult> Second(PackageIndexingContext context, PackageIndexingDelegate next)
            {
                Assert.Equal(1, counter);
                counter++;

                var result = await next();

                Assert.Equal(2, counter);
                Assert.Equal(PackageIndexingStatus.Success, result.Status);
                Assert.Empty(result.Messages);

                result.Status = PackageIndexingStatus.InvalidPackage;
                result.Messages.Add("Hello from second");
                counter++;
                return result;
            }
        }

        [Fact]
        public async Task StreamsAreRewinded()
        {
            SetupMiddlewares(First, Second);

            await _target.IndexAsync(PackageData.Default, _cancellationToken);

            Task<PackageIndexingResult> First(PackageIndexingContext context, PackageIndexingDelegate next)
            {
                Assert.Equal(0, context.PackageStream.Position);
                Assert.Equal(0, context.NuspecStream.Position);

                context.PackageStream.Position = 1;
                context.NuspecStream.Position = 1;

                return next();
            }

            Task<PackageIndexingResult> Second(PackageIndexingContext context, PackageIndexingDelegate next)
            {
                Assert.Equal(0, context.PackageStream.Position);
                Assert.Equal(0, context.NuspecStream.Position);

                return next();
            }
        }

        private void SetupMiddlewares(
            params Func<PackageIndexingContext, PackageIndexingDelegate, Task<PackageIndexingResult>>[] middlewares)
        {
            var mocks = new List<IPackageIndexingMiddleware>();
            foreach (var middleware in middlewares)
            {
                var mock = new Mock<IPackageIndexingMiddleware>();
                mock
                    .Setup(x => x.IndexAsync(It.IsAny<PackageIndexingContext>(), It.IsAny<PackageIndexingDelegate>()))
                    .Returns(middleware);

                mocks.Add(mock.Object);
            }

            _services
                .Setup(x => x.GetService(typeof(IEnumerable<IPackageIndexingMiddleware>)))
                .Returns(mocks);
        }
    }
}
