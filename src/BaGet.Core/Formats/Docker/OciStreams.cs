using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>Small helpers over request bodies.</summary>
internal static class OciStreams
{
    /// <summary>
    /// Null when <paramref name="stream"/> holds no byte, else a forward-only stream with the same content (the byte looked at is
    /// replayed). Lets a registry skip appending an empty chunk, which not every store accepts as a part.
    /// </summary>
    public static async Task<Stream?> NonEmptyAsync(Stream stream, CancellationToken cancellationToken)
    {
        var first = new byte[1];
        var read = await stream.ReadAsync(first.AsMemory(), cancellationToken).ConfigureAwait(false);

        return read == 0 ? null : new PrefixedStream(first[0], stream);
    }

    private sealed class PrefixedStream : Stream
    {
        private readonly byte _first;
        private readonly Stream _rest;
        private bool _firstRead;

        public PrefixedStream(byte first, Stream rest)
        {
            _first = first;
            _rest = rest;
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

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0) return 0;

            if (!_firstRead)
            {
                _firstRead = true;
                buffer[0] = _first;

                return 1;
            }

            return _rest.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0) return new ValueTask<int>(0);

            if (!_firstRead)
            {
                _firstRead = true;
                buffer.Span[0] = _first;

                return new ValueTask<int>(1);
            }

            return _rest.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
