using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGet.Core.Tests.Storage;

public class SymbolStorageKeyTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("not alphanumeric!!")]
    public async Task AKeyThatCannotBeAKeyIsAMissNotAnError(string key)
    {
        var storage = new SymbolStorageService(new FileStorageService(new TempOptions()));

        Assert.Null(await storage.GetPortablePdbContentStreamOrNullAsync("a.pdb", key));
    }

    private sealed class TempOptions : IOptionsSnapshot<FileSystemStorageOptions>
    {
        public FileSystemStorageOptions Value { get; } = new() { Path = System.IO.Path.GetTempPath() };
        public FileSystemStorageOptions Get(string? name) => Value;
    }
}
