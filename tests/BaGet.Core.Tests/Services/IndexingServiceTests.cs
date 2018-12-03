using System.Threading.Tasks;
using BaGet.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class IndexingServiceTests
    {
        private readonly Mock<IPackageService> _packages;
        private readonly Mock<IPackageStorageService> _storage;
        private readonly Mock<ISearchService> _search;
        private readonly IndexingService _target;

        public IndexingServiceTests()
        {
            _packages = new Mock<IPackageService>();
            _storage = new Mock<IPackageStorageService>();
            _search = new Mock<ISearchService>();

            _target = new IndexingService(
                _packages.Object,
                _storage.Object,
                _search.Object,
                Mock.Of<ILogger<IndexingService>>());
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
