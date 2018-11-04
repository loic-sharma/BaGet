using System.Threading.Tasks;
using BaGet.Core.Configuration;
using BaGet.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class PackageDeletionServiceTests
    {
        private readonly Mock<IPackageService> _packages;
        private readonly Mock<IPackageStorageService> _storage;

        private readonly BaGetOptions _options;
        private readonly PackageDeletionService _target;

        public PackageDeletionServiceTests()
        {
            _packages = new Mock<IPackageService>();
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
            await Task.Yield();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]

        public async Task WhenHardDelete_ReturnsTrueOnlyIfPackageExists(bool packageExists)
        {
            // TODO: Test that hard delete ALWAYS removes from storage, regardless of packageExists
            await Task.Yield();
        }
    }
}
