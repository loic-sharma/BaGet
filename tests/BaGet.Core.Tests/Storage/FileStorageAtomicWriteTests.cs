using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGet.Core.Tests.Storage;

public class FileStorageAtomicWriteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "baget-atomic-" + Guid.NewGuid().ToString("N"));
    private readonly FileStorageService _storage;

    public FileStorageAtomicWriteTests()
    {
        Directory.CreateDirectory(_root);
        _storage = new FileStorageService(new Snapshot(new FileSystemStorageOptions { Path = _root }));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task AFailedPutLeavesNothingAtThePathSoARetrySucceeds()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<IOException>(() => _storage.PutAsync("packages/a.nupkg", new FailingStream(), "application/octet-stream", ct));

        Assert.False(File.Exists(Path.Combine(_root, "packages", "a.nupkg")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "packages")));

        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        Assert.Equal(StoragePutResult.Success, await _storage.PutAsync("packages/a.nupkg", content, "application/octet-stream", ct));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(_root, "packages", "a.nupkg"), ct));
    }

    [Fact]
    public async Task AnExistingFileIsStillAnAlreadyExistsOrAConflict()
    {
        var ct = TestContext.Current.CancellationToken;

        using (var first = new MemoryStream(new byte[] { 1, 2, 3 }))
            Assert.Equal(StoragePutResult.Success, await _storage.PutAsync("x/f.bin", first, "application/octet-stream", ct));
        using (var same = new MemoryStream(new byte[] { 1, 2, 3 }))
            Assert.Equal(StoragePutResult.AlreadyExists, await _storage.PutAsync("x/f.bin", same, "application/octet-stream", ct));
        using (var other = new MemoryStream(new byte[] { 9 }))
            Assert.Equal(StoragePutResult.Conflict, await _storage.PutAsync("x/f.bin", other, "application/octet-stream", ct));

        Assert.Equal(new[] { "x/f.bin" }, (await _storage.ListAsync("x", ct)).ToArray());
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "x")));
    }

    private sealed class Snapshot : IOptionsSnapshot<FileSystemStorageOptions>
    {
        public Snapshot(FileSystemStorageOptions value) => Value = value;
        public FileSystemStorageOptions Value { get; }
        public FileSystemStorageOptions Get(string? name) => Value;
    }

    private sealed class FailingStream : MemoryStream
    {
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            => throw new IOException("connection lost");
    }
}
