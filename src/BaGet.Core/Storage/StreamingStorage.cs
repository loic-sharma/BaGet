using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>Helpers shared by the <see cref="IStreamingStorageService"/> providers and their callers.</summary>
public static class StreamingStorage
{
    /// <summary>The folder (and key prefix) in which providers keep uploads that are not committed yet.</summary>
    public const string UploadsFolder = ".uploads";

    /// <summary>Whether <paramref name="storage"/> can hold streaming uploads and range reads.</summary>
    public static bool IsSupported(IStorageService? storage) => storage is IStreamingStorageService;

    /// <summary>Returns <paramref name="storage"/> as a streaming store.</summary>
    /// <param name="storage">The configured store.</param>
    /// <param name="purpose">What needs it, for the message ("Docker repositories").</param>
    /// <exception cref="NotSupportedException">The store cannot stream.</exception>
    public static IStreamingStorageService Require(IStorageService? storage, string purpose)
        => storage as IStreamingStorageService
            ?? throw new NotSupportedException(
                $"{purpose} need a storage provider that streams large content (multipart upload and range reads); " +
                $"{(storage == null ? "no storage is configured" : storage.GetType().Name + " does not")}. " +
                "Use Storage:Type FileSystem, AwsS3, AzureBlobStorage, GoogleCloud or AliyunOss.");

    /// <summary>The SHA-256 of a stream, lower-case hex, read once in small blocks.</summary>
    public static async Task<string> Sha256HexAsync(Stream content, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>The SHA-256 of bytes already in memory, lower-case hex.</summary>
    public static string Sha256Hex(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>The SHA-256 (hex) of what <paramref name="storage"/> holds at <paramref name="path"/>, streamed; null when absent.</summary>
    public static async Task<string?> Sha256HexAsync(IStreamingStorageService storage, string path, CancellationToken cancellationToken = default)
    {
        using var stream = await storage.GetRangeAsync(path, 0, null, cancellationToken).ConfigureAwait(false);
        return stream == null ? null : await Sha256HexAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="value"/> is a lower-case hex SHA-256.</summary>
    public static bool IsSha256Hex(string? value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        }

        return true;
    }

    /// <summary>Throws unless <paramref name="expected"/> is null or equals <paramref name="actual"/>.</summary>
    /// <exception cref="StorageDigestMismatchException">They differ.</exception>
    public static void VerifyDigest(string? expected, string actual)
    {
        if (expected != null && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageDigestMismatchException(expected, actual);
        }
    }

    /// <summary>Validates <paramref name="offset"/>/<paramref name="length"/> against a blob of <paramref name="size"/> bytes and returns the byte count to read.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Negative, or the offset is past the end.</exception>
    public static long ResolveRange(long size, long offset, long? length)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (offset > size) throw new ArgumentOutOfRangeException(nameof(offset), "The offset is past the end of the blob.");

        return Math.Min(length ?? long.MaxValue, size - offset);
    }
}

/// <summary>Stream helpers for providers.</summary>
public static class StreamingStreams
{
    /// <summary>A read-only stream over <paramref name="memory"/> without copying it when it is backed by an array.</summary>
    public static Stream AsStream(ReadOnlyMemory<byte> memory)
        => System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array != null
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(memory.ToArray(), writable: false);

    /// <summary>
    /// Copies <paramref name="content"/> to a temporary file and returns it, positioned at 0 and seekable; disposing it deletes the file.
    /// Providers upload from it so a large object is never held in managed memory.
    /// </summary>
    public static async Task<Stream> SpoolAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        var file = new FileStream(
            Path.Combine(Path.GetTempPath(), "baget-spool-" + Guid.NewGuid().ToString("N")),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await content.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>A read stream that disposes the object that owns it (a download response) with it.</summary>
public sealed class OwnedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly IDisposable? _owner;

    /// <param name="inner">The stream to read.</param>
    /// <param name="owner">Disposed with the stream; may be null.</param>
    public OwnedReadStream(Stream inner, IDisposable? owner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _owner = owner;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

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
        if (disposing)
        {
            _inner.Dispose();
            _owner?.Dispose();
        }

        base.Dispose(disposing);
    }
}
