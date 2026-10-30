using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Keeps the running SHA-256 of each streaming upload on this node, so the digest is known the moment the last byte arrived
/// and a 5 GB layer is not read a second time. SHA-256 state cannot be saved, so a node that did not see every chunk (restart,
/// another node, a failed chunk) loses the entry; <see cref="TryComplete"/> then answers null and the provider recomputes the
/// digest from the stored content, streaming.
/// </summary>
public sealed class UploadDigestTracker
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromHours(2);

    /// <summary>The tracker the providers share (their instances are transient).</summary>
    public static UploadDigestTracker Shared { get; } = new();

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>The uploads being tracked (for tests and the idle sweep).</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Wraps <paramref name="chunk"/> so what the provider reads is hashed. Call <see cref="TrackedChunk.Complete"/> with the
    /// upload's length after storing it, or <see cref="TrackedChunk.Fail"/> when storing failed.
    /// </summary>
    /// <param name="uploadId">The upload.</param>
    /// <param name="offset">Where the chunk starts.</param>
    /// <param name="chunk">The chunk.</param>
    public TrackedChunk Track(string uploadId, long offset, Stream chunk)
    {
        if (uploadId == null) throw new ArgumentNullException(nameof(uploadId));
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));

        Sweep();

        Entry entry;
        if (offset == 0)
        {
            entry = new Entry();
            if (_entries.TryGetValue(uploadId, out var previous)) previous.Dispose();
            _entries[uploadId] = entry;
        }
        else if (_entries.TryGetValue(uploadId, out var existing) && existing.Valid && existing.Length == offset)
        {
            entry = existing;
        }
        else
        {
            // This node does not hold the bytes before the chunk: the digest will be recomputed at commit.
            entry = new Entry { Valid = false };
            if (_entries.TryGetValue(uploadId, out var previous)) previous.Dispose();
            _entries[uploadId] = entry;
        }

        entry.Touched = DateTime.UtcNow;
        return new TrackedChunk(entry, chunk);
    }

    /// <summary>
    /// The SHA-256 (lower-case hex) of the upload if it is known and covers exactly <paramref name="totalLength"/> bytes;
    /// null otherwise. Forgets the upload either way.
    /// </summary>
    public string? TryComplete(string uploadId, long totalLength)
    {
        if (!_entries.TryRemove(uploadId, out var entry)) return null;

        using (entry)
        {
            if (!entry.Valid || entry.Length != totalLength) return null;

            return Convert.ToHexString(entry.Hash.GetHashAndReset()).ToLowerInvariant();
        }
    }

    /// <summary>Drops the upload's state (abort, commit).</summary>
    public void Forget(string uploadId)
    {
        if (_entries.TryRemove(uploadId, out var entry)) entry.Dispose();
    }

    private void Sweep()
    {
        if (_entries.Count < 64) return;

        var limit = DateTime.UtcNow - IdleLimit;
        foreach (var pair in _entries.Where(p => p.Value.Touched < limit).ToList())
        {
            if (_entries.TryRemove(pair.Key, out var entry)) entry.Dispose();
        }
    }

    internal sealed class Entry : IDisposable
    {
        public IncrementalHash Hash { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long Length;

        public bool Valid = true;

        public DateTime Touched = DateTime.UtcNow;

        public void Dispose() => Hash.Dispose();
    }

    /// <summary>A chunk stream that hashes what is read from it.</summary>
    public sealed class TrackedChunk : Stream
    {
        private readonly Entry _entry;
        private readonly Stream _inner;

        internal TrackedChunk(Entry entry, Stream inner)
        {
            _entry = entry;
            _inner = inner;
        }

        /// <summary>The chunk was stored; <paramref name="uploadLength"/> is what the store now holds.</summary>
        public void Complete(long uploadLength)
        {
            // A mismatch means the provider stored more or less than was read: the running hash cannot be trusted.
            if (_entry.Valid && _entry.Length != uploadLength) _entry.Valid = false;
        }

        /// <summary>Storing the chunk failed or was cut: part of it may have been hashed without being stored.</summary>
        public void Fail() => _entry.Valid = false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            Account(buffer.AsSpan(offset, read));
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Account(buffer.Span.Slice(0, read));
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private void Account(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0 || !_entry.Valid) return;

            _entry.Hash.AppendData(bytes);
            _entry.Length += bytes.Length;
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
    }
}
