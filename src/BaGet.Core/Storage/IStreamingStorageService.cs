using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// An optional capability of an <see cref="IStorageService"/>: store content of any size without ever holding it in memory,
/// in several requests, and read it back by range. Docker layers reach many gigabytes and arrive as chunked uploads
/// (<c>PATCH</c> with <c>Content-Range</c>), are verified against a digest and are served with <c>HEAD</c> and range
/// <c>GET</c>; <see cref="IStorageService.PutAsync"/> takes a whole stream in one call and the providers buffer it.
/// </summary>
/// <remarks>
/// <para><b>Life of an upload.</b> <see cref="BeginUploadAsync"/> returns an opaque id; <see cref="AppendAsync"/> adds bytes at
/// the offset the store already holds (a different offset is refused with <see cref="StorageUploadOffsetException"/>, which
/// carries the real length so a client can resume); <see cref="CommitAsync"/> makes the content visible at its final path and
/// removes the upload; <see cref="AbortAsync"/> discards it. The final path is only given at commit because a content-addressed
/// path (a digest) is not known before the last byte. An upload that is neither committed nor aborted stays in the store: the
/// caller owns its lifetime (sessions expire, the caller aborts).</para>
/// <para><b>Memory.</b> At most one part buffer per request (a few MiB, never the blob). The SHA-256 is computed while the bytes
/// stream through (<see cref="UploadDigestTracker"/>); when a node lost that running state (restart, another node took the next
/// chunk) the digest is recomputed from the stored content at commit, still streaming.</para>
/// <para><b>Concurrency.</b> One writer per upload; the same upload appended by two requests at once is undefined (the
/// protocols forbid it). Different uploads, and reads, are independent.</para>
/// <para>A caller must not cast and assume: a store that does not implement this (<see cref="StreamingStorage.IsSupported"/>)
/// cannot hold a format that needs it, see <see cref="IFormatModule.RequiresStreamingStorage"/>.</para>
/// </remarks>
public interface IStreamingStorageService : IStorageService
{
    /// <summary>Starts an empty upload.</summary>
    /// <param name="contentType">The content type the committed blob will carry.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>An opaque id (URL-safe), valid until committed or aborted.</returns>
    Task<string> BeginUploadAsync(string contentType, CancellationToken cancellationToken = default);

    /// <summary>Appends <paramref name="chunk"/> to the upload.</summary>
    /// <param name="uploadId">The id from <see cref="BeginUploadAsync"/>.</param>
    /// <param name="offset">Where the chunk starts; must equal the upload's current length.</param>
    /// <param name="chunk">The bytes, read once and forward only (it may be a request body).</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The upload's length after the chunk.</returns>
    /// <exception cref="StorageUploadNotFoundException">No such upload.</exception>
    /// <exception cref="StorageUploadOffsetException"><paramref name="offset"/> is not the upload's length.</exception>
    Task<long> AppendAsync(string uploadId, long offset, Stream chunk, CancellationToken cancellationToken = default);

    /// <summary>The upload's current length (what a client resumes from), or null when there is no such upload.</summary>
    Task<long?> GetUploadLengthAsync(string uploadId, CancellationToken cancellationToken = default);

    /// <summary>Stores the upload at <paramref name="path"/> and removes it.</summary>
    /// <param name="uploadId">The id from <see cref="BeginUploadAsync"/>.</param>
    /// <param name="path">The final path, as for <see cref="IStorageService.PutAsync"/>.</param>
    /// <param name="expectedSha256">The lower-case hex SHA-256 the content must have, or null to skip the check.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>
    /// <see cref="StoragePutResult.Success"/>; <see cref="StoragePutResult.AlreadyExists"/> when the path holds identical content
    /// (the upload is discarded); <see cref="StoragePutResult.Conflict"/> when it holds different content (discarded as well).
    /// </returns>
    /// <exception cref="StorageUploadNotFoundException">No such upload.</exception>
    /// <exception cref="StorageDigestMismatchException">The content does not have <paramref name="expectedSha256"/>; nothing is
    /// stored at <paramref name="path"/> and the upload is discarded.</exception>
    Task<StorageCommitResult> CommitAsync(string uploadId, string path, string? expectedSha256, CancellationToken cancellationToken = default);

    /// <summary>Discards the upload and what it holds; unknown ids are ignored.</summary>
    Task AbortAsync(string uploadId, CancellationToken cancellationToken = default);

    /// <summary>What a <c>HEAD</c> needs: the size (and type, where the store keeps it), or null when the path holds nothing.</summary>
    Task<StorageBlobInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <paramref name="length"/> bytes from <paramref name="offset"/> (to the end when null) as a stream that is never
    /// buffered whole, or null when the path holds nothing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is past the end, or an argument is negative.</exception>
    Task<Stream?> GetRangeAsync(string path, long offset, long? length, CancellationToken cancellationToken = default);
}

/// <summary>A stored blob as <see cref="IStreamingStorageService.GetInfoAsync"/> reports it.</summary>
/// <param name="Length">Size in bytes.</param>
/// <param name="ContentType">The stored content type, or null when the store does not keep one (the file system).</param>
public sealed record StorageBlobInfo(long Length, string? ContentType);

/// <summary>The outcome of <see cref="IStreamingStorageService.CommitAsync"/>.</summary>
/// <param name="Result">Whether the content was stored, was already there, or clashed.</param>
/// <param name="Length">Size in bytes of the committed content.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the committed content.</param>
public sealed record StorageCommitResult(StoragePutResult Result, long Length, string Sha256);

/// <summary>The upload id is unknown (never begun, committed, aborted or lost).</summary>
public sealed class StorageUploadNotFoundException : Exception
{
    /// <param name="uploadId">The id asked for.</param>
    public StorageUploadNotFoundException(string uploadId)
        : base("The upload does not exist.") => UploadId = uploadId;

    /// <summary>The id asked for.</summary>
    public string UploadId { get; }
}

/// <summary>A chunk did not start where the upload ends (OCI answers <c>416</c> with this length).</summary>
public sealed class StorageUploadOffsetException : Exception
{
    /// <param name="actualLength">What the store holds.</param>
    /// <param name="requestedOffset">Where the chunk claimed to start.</param>
    public StorageUploadOffsetException(long actualLength, long requestedOffset)
        : base($"The chunk starts at {requestedOffset} but the upload holds {actualLength} bytes.")
    {
        ActualLength = actualLength;
        RequestedOffset = requestedOffset;
    }

    /// <summary>What the store holds.</summary>
    public long ActualLength { get; }

    /// <summary>Where the chunk claimed to start.</summary>
    public long RequestedOffset { get; }
}

/// <summary>The committed content is not what the caller announced.</summary>
public sealed class StorageDigestMismatchException : Exception
{
    /// <param name="expected">The announced SHA-256.</param>
    /// <param name="actual">The computed SHA-256.</param>
    public StorageDigestMismatchException(string expected, string actual)
        : base("The content does not have the expected digest.")
    {
        Expected = expected;
        Actual = actual;
    }

    /// <summary>The announced SHA-256 (hex).</summary>
    public string Expected { get; }

    /// <summary>The computed SHA-256 (hex).</summary>
    public string Actual { get; }
}
