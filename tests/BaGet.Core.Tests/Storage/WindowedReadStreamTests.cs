using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Storage;

/// <summary>
/// <see cref="WindowedReadStream"/>: a long ranged read is cut into bounded requests and a request that fails midway costs one
/// window, resumed at the exact byte already delivered (found by the 5 GB prototype of B2, where one response was cut by the store).
/// </summary>
public class WindowedReadStreamTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private static byte[] Data(int length) => Enumerable.Range(0, length).Select(i => (byte)(i * 31 + i / 251)).ToArray();

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, 1000, Token);
        return buffer.ToArray();
    }

    /// <summary>A store: serves ranges of <c>data</c>, logging each request; <c>failAfter</c> bytes into the Nth request it cuts the response.</summary>
    private sealed class Store
    {
        public readonly List<(long From, long Size)> Requests = new();
        public Func<int, long?> CutAfter = _ => null;
        public byte[] Data = Array.Empty<byte>();

        public Task<Stream> OpenAsync(long from, long size, CancellationToken ct)
        {
            Requests.Add((from, size));
            Stream s = new MemoryStream(Data, (int)from, (int)size, writable: false);
            var cut = CutAfter(Requests.Count);
            return Task.FromResult(cut == null ? s : new CutStream(s, cut.Value));
        }
    }

    private sealed class CutStream : Stream
    {
        private readonly Stream _inner;
        private long _left;

        public CutStream(Stream inner, long left)
        {
            _inner = inner;
            _left = left;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_left <= 0) throw new IOException("The response ended prematurely.");
            var n = _inner.Read(buffer.Span.Slice(0, (int)Math.Min(buffer.Length, _left)));
            _left -= n;
            return new ValueTask<int>(n);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task TheRangeIsReadInBoundedWindowsAndComesBackWhole()
    {
        var store = new Store { Data = Data(10_000) };

        using var stream = new WindowedReadStream(store.OpenAsync, 1_000, 8_500, window: 3_000);
        var read = await ReadAllAsync(stream);

        Assert.Equal(store.Data.Skip(1_000).Take(8_500), read);
        Assert.Equal(new[] { (1_000L, 3_000L), (4_000L, 3_000L), (7_000L, 2_500L) }, store.Requests);
    }

    [Fact]
    public async Task AnEmptyRangeAsksForNothing()
    {
        var store = new Store { Data = Data(10) };

        using var stream = new WindowedReadStream(store.OpenAsync, 10, 0);

        Assert.Empty(await ReadAllAsync(stream));
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task AResponseCutMidwayIsRequestedAgainFromTheExactByte()
    {
        var store = new Store { Data = Data(10_000), CutAfter = n => n == 1 ? 1_234 : n == 3 ? 5 : null };

        using var stream = new WindowedReadStream(store.OpenAsync, 0, 10_000, window: 4_000);
        var read = await ReadAllAsync(stream);

        Assert.Equal(store.Data, read);
        Assert.Equal((1_234L, 4_000L), store.Requests[1]);
    }

    [Fact]
    public async Task ARangeThatMakesNoProgressIsReportedAfterTheRetries()
    {
        var store = new Store { Data = Data(10_000), CutAfter = _ => 0 };

        using var stream = new WindowedReadStream(store.OpenAsync, 0, 10_000, window: 4_000, retries: 2);

        await Assert.ThrowsAsync<IOException>(async () => _ = await stream.ReadAsync(new byte[100], Token));
        Assert.Equal(3, store.Requests.Count);
    }

    [Fact]
    public async Task ADisposedStreamCannotBeRead()
    {
        var store = new Store { Data = Data(100) };
        var stream = new WindowedReadStream(store.OpenAsync, 0, 100);
        stream.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => _ = await stream.ReadAsync(new byte[10], Token));
    }
}
