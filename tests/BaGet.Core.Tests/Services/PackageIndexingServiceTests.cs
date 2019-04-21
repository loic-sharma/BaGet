using System.Threading.Tasks;
using BaGet.Core.Configuration;
using BaGet.Core.Indexing;
using BaGet.Core.Search;
using BaGet.Core.State;
using BaGet.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class PackageIndexingServiceTests
    {
        private readonly Mock<IPackageService> _packages;
        private readonly Mock<IPackageStorageService> _storage;
        private readonly Mock<ISearchService> _search;
        private readonly PackageIndexingService _target;

        public PackageIndexingServiceTests()
        {
            _packages = new Mock<IPackageService>();
            _storage = new Mock<IPackageStorageService>();
            _search = new Mock<ISearchService>();

            _target = new PackageIndexingService(
                _packages.Object,
                _storage.Object,
                _search.Object,
                Mock.Of<IOptionsSnapshot<BaGetOptions>>(),
                Mock.Of<ILogger<PackageIndexingService>>());
        }

        // TODO: Add malformed package tests

        [Fact]
        public async Task WhenPackageAlreadyExists_ReturnsPackageAlreadyExists()
        {
            await Task.Yield();
        }

        [Fact]
        public async Task WhenDatabaseAddFailsBecausePackageAlreadyExists_ReturnsPackageAlreadyExists()
        {
            await Task.Yield();
        }

        [Fact]
        public async Task IndexesPackage()
        {
            await Task.Yield();
        }

        [Fact]
        public async Task WhenPackageHasNoReadme_SavesNullReadmeStream()
        {
            await Task.Yield();
        }

        [Fact]
        public async Task ThrowsWhenStorageSaveThrows()
        {
            await Task.Yield();
        }
    }
}
