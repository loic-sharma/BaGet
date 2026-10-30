using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Storage.Tests;

/// <summary>
/// A deterministic, forward-only stream of <c>length</c> pseudo-random bytes that is never held in memory and is reproducible, so a test can push gigabytes and still know the SHA-256 the store must report (<see cref="Sha256Of"/>); wrapped in a
/// <c>RangeReadStream</c> it yields one chunk of a chunked upload at a time.
/// </summary>
public sealed class PatternStream : Stream
{
    private readonly long _length;
    private readonly Random _random;
    private long _position;

    public PatternStream(long length, int seed = 1)
    {
        _length = length;
        _random = new Random(seed);
    }

    /// <summary>The first <paramref name="length"/> bytes of the pattern, in memory (small blobs only).</summary>
    public static byte[] Bytes(long length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>The SHA-256 (hex) of a pattern, computed without keeping it.</summary>
    public static string Sha256Of(long length, int seed = 1)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var random = new Random(seed);
        var buffer = new byte[1 << 20];
        for (long left = length; left > 0;)
        {
            var count = (int)Math.Min(buffer.Length, left);
            random.NextBytes(buffer.AsSpan(0, count));
            hash.AppendData(buffer, 0, count);
            left -= count;
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = (int)Math.Min(buffer.Length, _length - _position);
        _random.NextBytes(buffer.Span.Slice(0, count));
        _position += count;
        return new ValueTask<int>(count);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        count = (int)Math.Min(count, _length - _position);
        _random.NextBytes(buffer.AsSpan(offset, count));
        _position += count;
        return count;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
