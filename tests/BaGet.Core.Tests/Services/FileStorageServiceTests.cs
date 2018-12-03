using System;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    // TODO
    public class FileStorageServiceTests
    {
        public class GetAsync : FactsBase
        {
            [Fact]
            public async Task ThrowsIfPathDoesNotExist()
            {
                await Task.CompletedTask;
            }
        }

        public class GetDownloadUriAsync : FactsBase
        {
        }

        public class PutAsync : FactsBase
        {
        }

        public class DeleteAsync : FactsBase
        {
        }

        public class FactsBase : IDisposable
        {
            public void Dispose()
            {
                throw new NotImplementedException();
            }
        }
    }
}
