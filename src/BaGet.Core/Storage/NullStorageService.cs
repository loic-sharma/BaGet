using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// A minimal storage implementation, used for advanced scenarios. It is a streaming store that holds nothing: uploaded
/// bytes are counted and discarded, nothing is ever readable, and a commit finds no upload.
/// </summary>
public class NullStorageService : IStreamingStorageService, IListableStorageService
{
    public Task<string> BeginUploadAsync(string contentType, CancellationToken cancellationToken = default)
        => Task.FromResult(Guid.NewGuid().ToString("N"));

    public async Task<long> AppendAsync(string uploadId, long offset, Stream chunk, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[81920];
        var total = offset;
        int read;
        while ((read = await chunk.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0) total += read;
        return total;
    }

    public Task<long?> GetUploadLengthAsync(string uploadId, CancellationToken cancellationToken = default)
        => Task.FromResult<long?>(null);

    public Task<StorageCommitResult> CommitAsync(string uploadId, string path, string? expectedSha256, CancellationToken cancellationToken = default)
        => throw new StorageUploadNotFoundException(uploadId);

    public Task AbortAsync(string uploadId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<StorageBlobInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult<StorageBlobInfo?>(null);

    public Task<Stream?> GetRangeAsync(string path, long offset, long? length, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream?>(null);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// A store that holds nothing lists nothing, so a sweep over it has nothing to do.
    /// </summary>
    public Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        #pragma warning disable CS8625
        return Task.FromResult<Stream>(null);
        #pragma warning restore CS8625
    }

    public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
    {
        #pragma warning disable CS8625
        return Task.FromResult<Uri>(null);
        #pragma warning restore CS8625
    }

    public Task<StoragePutResult> PutAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StoragePutResult.Success);
    }
}