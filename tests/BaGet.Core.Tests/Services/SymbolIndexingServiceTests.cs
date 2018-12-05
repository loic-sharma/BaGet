using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Services
{
    public class SymbolIndexingServiceTests
    {
        [Fact]
        public async Task IfSnupkgHasEntryThatExtractsOutsideOfExtractionPath_ReturnsInvalidSymbolPackage()
        {
            await Task.CompletedTask;
        }

        [Fact]
        public async Task IfSnupkgContainsEntryWithInvalidExtension_ReturnsInvalidSymbolPackage()
        {
            await Task.CompletedTask;
        }

        [Fact]
        public async Task IfSnupkgDoesNotHaveCorrespondingNupkg_ReturnsPackageNotFound()
        {
            await Task.CompletedTask;
        }

        [Fact]
        public async Task IfSnupkgContainsInvalidPortablePdb_ReturnsInvalidSymbolPackage()
        {
            // Have two pdb entries, the first of which is valid. Ensure nothing is persisted.
            await Task.CompletedTask;
        }

        [Fact]
        public async Task IfSnupkgPdbDoesNotHaveCorrespondingNupkgDll_ReturnsInvalidSymbolPackage()
        {
            await Task.CompletedTask;
        }

        [Fact]
        public async Task SavesPdbs()
        {
            await Task.CompletedTask;
        }
    }
}
