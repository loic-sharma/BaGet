using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Catalog.Storage;

/// <summary>
/// Counts the bytes read through it, since a storage stream may not know its length.
/// </summary>
internal sealed class CountingStream : Stream
{
    private readonly Stream? _source;

    public CountingStream(Stream? source) => _source = source;

    public long BytesRead { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        #pragma warning disable CS8602
        var read = _source.Read(buffer, offset, count);
        #pragma warning restore CS8602
        BytesRead += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        #pragma warning disable CS8602
        var read = await _source.ReadAsync(buffer, cancellationToken);
        #pragma warning restore CS8602
        BytesRead += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
