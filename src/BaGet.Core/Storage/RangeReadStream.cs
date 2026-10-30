using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>Exposes at most <c>length</c> bytes of a stream, from where it is; disposing disposes the inner stream.</summary>
public sealed class RangeReadStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    /// <param name="inner">The stream, already positioned at the range start.</param>
    /// <param name="length">How many bytes to expose.</param>
    public RangeReadStream(Stream inner, long length)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        _remaining = length;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0 || buffer.Length == 0) return 0;

        var read = await _inner.ReadAsync(buffer.Slice(0, (int)Math.Min(buffer.Length, _remaining)), cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0) return 0;

        var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
        _remaining -= read;
        return read;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
