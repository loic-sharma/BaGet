using System.IO;
using System.Threading.Tasks;
using BaGet.Core.Services;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class FilePackageStorageServiceTests
    {
        public class SavePackageContentAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfPackageStreamIsNull()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task ThrowsIfNuspecStreamIsNull()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task DoesNotThrowIfReadmeStreamIsNull()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task SavesContent()
            {
                // TODO: Should lowercase id/version
                // TODO: Should normalize version
                // TODO: Test that a directory was created for the package
                // TODO: Verify content.
                await Task.Yield();
            }

            [Fact]
            public async Task DoesNotThrowIfContentAlreadyExistsAndContentsMatch()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task ThrowsIfContentAlreadyExistsButContentsDoNotMatch()
            {
                await Task.Yield();
            }
        }

        public class GetPackageStreamAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfDoesntExist()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task GetsStream()
            {
                // TODO: Should lowercase id/version
                // TODO: Should normalize version
                await Task.Yield();
            }
        }

        public class GetNuspecStreamAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfDoesntExist()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task GetsStream()
            {
                // TODO: Should lowercase id/version
                // TODO: Should normalize version
                await Task.Yield();
            }
        }

        public class GetReadmeStreamAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfDoesntExist()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task GetsStream()
            {
                // TODO: Should lowercase id/version
                // TODO: Should normalize version
                await Task.Yield();
            }
        }

        public class DeleteAsync : FactsBase
        {
            [Fact]
            public async Task DoesNotThrowIfPackagePathDoesNotExist()
            {
                await Task.Yield();
            }

            [Theory]
            [InlineData(false, false, false)]
            [InlineData(false, false, true)]
            [InlineData(false, true, false)]
            [InlineData(false, true, true)]
            [InlineData(true, false, false)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(true, true, true)]
            public async Task DeletesAnyExistingContent(bool packageExists, bool nuspecExists, bool readmeExists)
            {
                // TODO: Should lowercase id/version
                // TODO: Should normalize version
                await Task.Yield();
            }
        }

        public class FactsBase
        {
            protected readonly string _storePath;
            protected readonly FilePackageStorageService _target;

            public FactsBase()
            {
                _storePath = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
                _target = new FilePackageStorageService(_storePath);
            }
        }
    }
}
