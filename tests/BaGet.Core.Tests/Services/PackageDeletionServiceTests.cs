using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class PackageDeletionServiceTests
    {
        private static readonly string PackageId = "Package";
        private static readonly NuGetVersion PackageVersion = new NuGetVersion("1.0.0");

        private readonly Mock<IPackageDatabase> _packages;
        private readonly Mock<IPackageStorageService> _storage;

        private readonly BaGetOptions _options;
        private readonly PackageDeletionService _target;

        public PackageDeletionServiceTests()
        {
            _packages = new Mock<IPackageDatabase>();
            _storage = new Mock<IPackageStorageService>();
            _options = new BaGetOptions();

            var optionsSnapshot = new Mock<IOptionsSnapshot<BaGetOptions>>();
            optionsSnapshot.Setup(o => o.Value).Returns(_options);

            _target = new PackageDeletionService(
                _packages.Object,
                _storage.Object,
                optionsSnapshot.Object,
                Mock.Of<ILogger<PackageDeletionService>>());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WhenUnlist_ReturnsTrueOnlyIfPackageExists(bool packageExists)
        {
            // Arrange
            var cancellationToken = CancellationToken.None;
            _options.PackageDeletionBehavior = PackageDeletionBehavior.Unlist;

            _packages
                .Setup(p => p.UnlistPackageAsync(PackageId, PackageVersion, cancellationToken))
                .ReturnsAsync(packageExists);

            // Act
            var result = await _target.TryDeletePackageAsync(PackageId, PackageVersion, cancellationToken);

            // Assert
            Assert.Equal(packageExists, result);

            _packages.Verify(
                p => p.UnlistPackageAsync(PackageId, PackageVersion, cancellationToken),
                Times.Once);

            _packages.Verify(
                p => p.HardDeletePackageAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _storage.Verify(
                s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WhenHardDelete_ReturnsTrueOnlyIfPackageExists(bool packageExists)
        {
            // Arrange
            _options.PackageDeletionBehavior = PackageDeletionBehavior.HardDelete;

            var step = 0;
            var databaseStep = -1;
            var storageStep = -1;
            var cancellationToken = CancellationToken.None;

            _packages
                .Setup(p => p.HardDeletePackageAsync(PackageId, PackageVersion, cancellationToken))
                .Callback(() => databaseStep = step++)
                .ReturnsAsync(packageExists);

            _storage
                .Setup(s => s.DeleteAsync(PackageId, PackageVersion, cancellationToken))
                .Callback(() => storageStep = step++)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _target.TryDeletePackageAsync(PackageId, PackageVersion, cancellationToken);

            // Assert - The database step MUST happen before the storage step.
            Assert.Equal(packageExists, result);
            Assert.Equal(0, databaseStep);
            Assert.Equal(1, storageStep);

            // The storage deletion should happen even if the package couldn't
            // be found in the database. This ensures consistency.
            _packages.Verify(
                p => p.HardDeletePackageAsync(PackageId, PackageVersion, cancellationToken),
                Times.Once);
            _storage.Verify(
                s => s.DeleteAsync(PackageId, PackageVersion, cancellationToken),
                Times.Once);

            _packages.Verify(
                p => p.UnlistPackageAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
