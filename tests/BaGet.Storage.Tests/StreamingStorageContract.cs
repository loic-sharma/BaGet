using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Xunit;

namespace BaGet.Storage.Tests;

/// <summary>
/// What every <see cref="IStreamingStorageService"/> must do, run against each provider (the file system in-process, the cloud
/// providers against their emulators). Parts are 8-16 MiB in the providers, so blobs of 20-40 MiB cut by ragged chunks cross
/// every boundary: a chunk inside a part, a chunk ending a part, a chunk spanning several, and a tail.
/// </summary>
public abstract class StreamingStorageContract
{
    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A store whose blobs cannot collide with another test's.</summary>
    protected abstract Task<IStreamingStorageService> CreateStoreAsync();

    /// <summary>False for an emulator that keeps answering for a cancelled resumable session (the real service answers 404/499).</summary>
    protected virtual bool CancelledSessionsAreGone => true;

    /// <summary>False for an emulator that ignores <c>ifGenerationMatch=0</c> on a copy (the real service answers 412).</summary>
    protected virtual bool ConditionalCopyIsEnforced => true;

    private const int MiB = 1024 * 1024;

    private static async Task<(string Id, long Length)> UploadAsync(IStreamingStorageService store, long total, int seed, params long[] chunks)
    {
        var id = await store.BeginUploadAsync("application/octet-stream", Token);
        using var source = new PatternStream(total, seed);
        long offset = 0;
        foreach (var size in chunks)
        {
            var take = Math.Min(size, total - offset);
            using var chunk = new RangeReadStream(NonDisposing(source), take);
            offset = await store.AppendAsync(id, offset, chunk, Token);
            Assert.Equal(Math.Min(offset, total), offset);
        }

        return (id, offset);
    }

    // RangeReadStream disposes what it wraps; the pattern must outlive each chunk.
    private static Stream NonDisposing(Stream inner) => new KeepOpen(inner);

    private sealed class KeepOpen : Stream
    {
        private readonly Stream _inner;

        public KeepOpen(Stream inner) => _inner = inner;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
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

    private static string NewPath() => "blobs/" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task RaggedChunksAcrossPartBoundariesStoreTheExactBlobAndItsDigest()
    {
        var store = await CreateStoreAsync();
        const long total = 41 * MiB + 123;
        var (id, length) = await UploadAsync(store, total, 11, 3 * MiB, 20 * MiB, 1, 17 * MiB, 5 * MiB);
        Assert.Equal(total, length);
        Assert.Equal(total, await store.GetUploadLengthAsync(id, Token));

        var path = NewPath();
        var sha = PatternStream.Sha256Of(total, 11);
        var result = await store.CommitAsync(id, path, sha, Token);

        Assert.Equal(StoragePutResult.Success, result.Result);
        Assert.Equal(total, result.Length);
        Assert.Equal(sha, result.Sha256);
        Assert.Equal(total, (await store.GetInfoAsync(path, Token))!.Length);
        Assert.Equal(sha, await StreamingStorage.Sha256HexAsync(store, path, Token));
        Assert.Null(await store.GetUploadLengthAsync(id, Token));
    }

    [Fact]
    public async Task ABlobSmallerThanAPartAndAnEmptyOneAreStored()
    {
        var store = await CreateStoreAsync();

        var (small, _) = await UploadAsync(store, 1000, 3, 400, 600);
        var smallPath = NewPath();
        var result = await store.CommitAsync(small, smallPath, PatternStream.Sha256Of(1000, 3), Token);
        Assert.Equal(StoragePutResult.Success, result.Result);
        Assert.Equal(1000, (await store.GetInfoAsync(smallPath, Token))!.Length);
        Assert.Equal(PatternStream.Sha256Of(1000, 3), await StreamingStorage.Sha256HexAsync(store, smallPath, Token));

        var empty = await store.BeginUploadAsync("application/octet-stream", Token);
        var emptyPath = NewPath();
        var emptyResult = await store.CommitAsync(empty, emptyPath, Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant(), Token);
        Assert.Equal(StoragePutResult.Success, emptyResult.Result);
        Assert.Equal(0, (await store.GetInfoAsync(emptyPath, Token))!.Length);
    }

    [Fact]
    public async Task AChunkAtTheWrongOffsetIsRefusedWithTheRealLengthAndTheUploadResumes()
    {
        var store = await CreateStoreAsync();
        var id = await store.BeginUploadAsync("application/octet-stream", Token);
        await store.AppendAsync(id, 0, new MemoryStream(PatternStream.Bytes(5000, 5)), Token);

        var ahead = await Assert.ThrowsAsync<StorageUploadOffsetException>(
            () => store.AppendAsync(id, 9000, new MemoryStream(new byte[10]), Token));
        Assert.Equal(5000, ahead.ActualLength);
        Assert.Equal(9000, ahead.RequestedOffset);

        var behind = await Assert.ThrowsAsync<StorageUploadOffsetException>(
            () => store.AppendAsync(id, 100, new MemoryStream(new byte[10]), Token));
        Assert.Equal(5000, behind.ActualLength);

        // The refused chunks changed nothing: resume at the reported length and finish.
        Assert.Equal(5000, await store.GetUploadLengthAsync(id, Token));
        using var rest = new PatternStream(7000, 5);
        var all = PatternStream.Bytes(7000, 5);
        await store.AppendAsync(id, 5000, new MemoryStream(all, 5000, 2000), Token);
        var path = NewPath();
        var result = await store.CommitAsync(id, path, Convert.ToHexString(SHA256.HashData(all)).ToLowerInvariant(), Token);
        Assert.Equal(7000, result.Length);
    }

    [Fact]
    public async Task AWrongDigestStoresNothingAndDiscardsTheUpload()
    {
        var store = await CreateStoreAsync();
        var (id, _) = await UploadAsync(store, 20 * MiB, 7, 8 * MiB, 12 * MiB);
        var path = NewPath();

        var e = await Assert.ThrowsAsync<StorageDigestMismatchException>(() => store.CommitAsync(id, path, new string('0', 64), Token));

        Assert.Equal(PatternStream.Sha256Of(20 * MiB, 7), e.Actual);
        Assert.Null(await store.GetInfoAsync(path, Token));
        if (CancelledSessionsAreGone) Assert.Null(await store.GetUploadLengthAsync(id, Token));
    }

    [Fact]
    public async Task TheDigestIsRecomputedFromTheStoredContentWhenThisNodeLostItsRunningHash()
    {
        var store = await CreateStoreAsync();
        var id = await store.BeginUploadAsync("application/octet-stream", Token);
        const long total = 30 * MiB;
        using var source = new PatternStream(total, 21);
        var offset = await store.AppendAsync(id, 0, new RangeReadStream(new KeepOpen(source), 18 * MiB), Token);

        // Another node (or a restart) took over: the running hash is gone.
        UploadDigestTracker.Shared.Forget(id);
        offset = await store.AppendAsync(id, offset, new RangeReadStream(new KeepOpen(source), 12 * MiB), Token);
        Assert.Equal(total, offset);

        var path = NewPath();
        var sha = PatternStream.Sha256Of(total, 21);
        Assert.Equal(sha, (await store.CommitAsync(id, path, sha, Token)).Sha256);
        Assert.Equal(sha, await StreamingStorage.Sha256HexAsync(store, path, Token));

        // And a wrong digest is still caught on that path.
        var other = await store.BeginUploadAsync("application/octet-stream", Token);
        await store.AppendAsync(other, 0, new MemoryStream(new byte[100]), Token);
        UploadDigestTracker.Shared.Forget(other);
        await store.AppendAsync(other, 100, new MemoryStream(new byte[100]), Token);
        await Assert.ThrowsAsync<StorageDigestMismatchException>(() => store.CommitAsync(other, NewPath(), new string('f', 64), Token));
    }

    [Fact]
    public async Task CommittingTheSameContentTwiceIsAlreadyExistsAndDifferentContentIsAConflict()
    {
        var store = await CreateStoreAsync();
        var path = NewPath();

        var (first, _) = await UploadAsync(store, 20 * MiB, 31, 20 * MiB);
        Assert.Equal(StoragePutResult.Success, (await store.CommitAsync(first, path, null, Token)).Result);

        if (!ConditionalCopyIsEnforced) return;

        var (same, _) = await UploadAsync(store, 20 * MiB, 31, 7 * MiB, 13 * MiB);
        Assert.Equal(StoragePutResult.AlreadyExists, (await store.CommitAsync(same, path, null, Token)).Result);

        var (different, _) = await UploadAsync(store, 20 * MiB, 32, 20 * MiB);
        Assert.Equal(StoragePutResult.Conflict, (await store.CommitAsync(different, path, null, Token)).Result);

        // The stored blob is still the first one.
        Assert.Equal(PatternStream.Sha256Of(20 * MiB, 31), await StreamingStorage.Sha256HexAsync(store, path, Token));
    }

    [Fact]
    public async Task RangeReadsReturnExactlyTheAskedBytes()
    {
        var store = await CreateStoreAsync();
        var all = PatternStream.Bytes(3 * MiB + 17, 41);
        var id = await store.BeginUploadAsync("application/octet-stream", Token);
        await store.AppendAsync(id, 0, new MemoryStream(all), Token);
        var path = NewPath();
        await store.CommitAsync(id, path, null, Token);

        async Task<byte[]> ReadAsync(long offset, long? length)
        {
            using var stream = await store.GetRangeAsync(path, offset, length, Token);
            Assert.NotNull(stream);
            using var buffer = new MemoryStream();
            await stream!.CopyToAsync(buffer, Token);
            return buffer.ToArray();
        }

        Assert.Equal(all, await ReadAsync(0, null));
        Assert.Equal(all.Skip(1000).Take(5000), await ReadAsync(1000, 5000));
        Assert.Equal(all.Skip(2 * MiB).ToArray(), await ReadAsync(2 * MiB, null));
        Assert.Equal(all.Skip(all.Length - 10).ToArray(), await ReadAsync(all.Length - 10, 1000));
        Assert.Empty(await ReadAsync(all.Length, null));
        Assert.Empty(await ReadAsync(5, 0));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.GetRangeAsync(path, all.Length + 1, null, Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.GetRangeAsync(path, -1, null, Token));
        Assert.Equal(all.Length, (await store.GetInfoAsync(path, Token))!.Length);
    }

    [Fact]
    public async Task AnUnknownPathHasNoInfoAndNoContent()
    {
        var store = await CreateStoreAsync();

        Assert.Null(await store.GetInfoAsync("blobs/none", Token));
        Assert.Null(await store.GetRangeAsync("blobs/none", 0, null, Token));
    }

    [Fact]
    public async Task AnAbortedUploadIsGoneAndCannotBeUsed()
    {
        var store = await CreateStoreAsync();
        var (id, _) = await UploadAsync(store, 20 * MiB, 51, 20 * MiB);

        await store.AbortAsync(id, Token);
        await store.AbortAsync(id, Token);

        if (!CancelledSessionsAreGone) return;
        Assert.Null(await store.GetUploadLengthAsync(id, Token));
        await Assert.ThrowsAsync<StorageUploadNotFoundException>(() => store.AppendAsync(id, 20 * MiB, new MemoryStream(new byte[1]), Token));
        await Assert.ThrowsAsync<StorageUploadNotFoundException>(() => store.CommitAsync(id, NewPath(), null, Token));
        await store.AbortAsync("not-an-upload-id", Token);
        Assert.Null(await store.GetUploadLengthAsync("not-an-upload-id", Token));
    }

    [Fact]
    public async Task ABlobIsStreamedThroughWithMemoryThatDoesNotGrowWithIt()
    {
        var store = await CreateStoreAsync();
        var total = LargeBlobBytes();
        const long chunkSize = 256L * MiB;

        using var probe = new MemoryProbe();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var id = await store.BeginUploadAsync("application/octet-stream", Token);
        using var source = new PatternStream(total, 61);
        long offset = 0;
        while (offset < total)
        {
            offset = await store.AppendAsync(id, offset, new RangeReadStream(new KeepOpen(source), Math.Min(chunkSize, total - offset)), Token);
        }

        var path = NewPath();
        var expected = PatternStream.Sha256Of(total, 61);
        var uploaded = clock.Elapsed;
        var result = await store.CommitAsync(id, path, expected, Token);
        var committed = clock.Elapsed;

        using (var stream = await store.GetRangeAsync(path, 0, null, Token))
        {
            Assert.Equal(expected, await StreamingStorage.Sha256HexAsync(stream!, Token));
        }

        var peakMiB = probe.PeakGrowth / MiB;
        Report($"{GetType().Name}: {total / MiB} MiB, upload {uploaded.TotalSeconds:F1}s, commit {(committed - uploaded).TotalSeconds:F1}s, " +
               $"read back {(clock.Elapsed - committed).TotalSeconds:F1}s, peak private memory growth {peakMiB} MiB");

        Assert.Equal(StoragePutResult.Success, result.Result);
        Assert.Equal(expected, result.Sha256);
        // Bounded: a few part buffers and the hashing/copy buffers, whatever the blob size.
        Assert.True(peakMiB < 256, $"Memory grew by {peakMiB} MiB while streaming {total / MiB} MiB.");
    }

    /// <summary>Bytes of the large-blob test: 96 MiB by default; <c>BAGET_STORAGE_LARGE_BYTES=5368709120</c> runs the 5 GB prototype of B2.</summary>
    protected static long LargeBlobBytes()
        => long.TryParse(Environment.GetEnvironmentVariable("BAGET_STORAGE_LARGE_BYTES"), out var bytes) && bytes > 0 ? bytes : 96L * MiB;

    private static void Report(string line)
    {
        Console.WriteLine(line);
        var file = Environment.GetEnvironmentVariable("BAGET_STORAGE_REPORT");
        if (!string.IsNullOrEmpty(file)) File.AppendAllText(file, line + Environment.NewLine);
    }
}
