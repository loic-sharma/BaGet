using System.IO;
using BaGet.Core.Services;
using Xunit;

namespace BaGet.Core.Tests
{
    public class FilePackageStorageServiceTests
    {
        [Fact]
        public void CreatesStoreDirectoryIfItDoesNotExist()
        {
            string validButNotExistingPath = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
            Assert.False(Directory.Exists(validButNotExistingPath));
            IPackageStorageService service = new FilePackageStorageService(validButNotExistingPath);
            Assert.False(Directory.Exists(validButNotExistingPath)); 
        }
    }
}
