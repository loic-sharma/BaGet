using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class SymbolStorageServiceTests
    {
        public class SavePortablePdbContentAsync
        {
            [Fact]
            public async Task ThrowsIfFileNameIsInvalid()
            {
                await Task.CompletedTask;
            }

            [Fact]
            public async Task ThrowsIfKeyIsInvalid()
            {
                await Task.CompletedTask;
            }

            [Fact]
            public async Task SavesPdb()
            {
                await Task.CompletedTask;
            }

            [Fact]
            public async Task IfPdbAlreadyExistsButHasDifferentContent_Fails()
            {
                await Task.CompletedTask;
            }

            [Fact]
            public async Task IfPdbAlreadyExistsButHasSameContent_Succeeds()
            {
                await Task.CompletedTask;
            }
        }

        public class GetPortablePdbContentStreamOrNullAsync
        {
            [Fact]
            public async Task ThrowsIfFileNameIsInvalid()
            {
                await Task.CompletedTask;
            }

            [Fact]
            public async Task ThrowsIfKeyIsInvalid()
            {
                await Task.CompletedTask;
            }

            [Fact]
            public async Task ReturnsNullIfPdbDoesNotExist()
            {
                await Task.CompletedTask;
            }
            
            [Fact]
            public async Task ReturnsPdb()
            {
                await Task.CompletedTask;
            }
        }
    }
}
