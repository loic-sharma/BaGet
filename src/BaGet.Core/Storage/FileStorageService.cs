using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// Stores content on disk. Streaming uploads (<see cref="IStreamingStorageService"/>) grow a file under
/// <see cref="StreamingStorage.UploadsFolder"/> in the store and are moved into place on commit (same volume, so atomic).
/// </summary>
public class FileStorageService : IStreamingStorageService, IListableStorageService
{
    // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
    private const int DefaultCopyBufferSize = 81920;

    /// <summary>Suffix of the temporary file a put writes before moving it into place; never listed.</summary>
    internal const string TemporarySuffix = ".baget-tmp";

    private readonly string _storePath;

    public FileStorageService(IOptionsSnapshot<FileSystemStorageOptions> options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        // Resolve relative path components ('.'/'..') and ensure there is a trailing slash.
        #pragma warning disable CS8604
        _storePath = Path.GetFullPath(options.Value.Path);
        #pragma warning restore CS8604
        if (!_storePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            _storePath += Path.DirectorySeparatorChar;
    }

    public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);
        var content = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        return Task.FromResult<Stream>(content);
    }

    public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Uri(GetFullPath(path));

        return Task.FromResult(result);
    }

    public async Task<StoragePutResult> PutAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrEmpty(contentType)) throw new ArgumentException("Content type is required", nameof(contentType));

        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);

        // Ensure that the path exists.
        #pragma warning disable CS8604
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        #pragma warning restore CS8604

        // Write to a temporary file in the same folder and move it into place, so a failed or cancelled copy
        // never leaves a truncated file at the final path (a later put would compare against it and conflict).
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + TemporarySuffix;

        try
        {
            using (var fileStream = File.Open(temporaryPath, FileMode.CreateNew))
            {
                await content.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
            }

            File.Move(temporaryPath, path);
            return StoragePutResult.Success;
        }
        catch (IOException) when (File.Exists(path))
        {
            using (var targetStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                content.Position = 0;
                return content.Matches(targetStream)
                    ? StoragePutResult.AlreadyExists
                    : StoragePutResult.Conflict;
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // GetFullPath keeps the prefix inside the store, exactly as for a read or write.
        var fullPath = GetFullPath(pathPrefix);

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var paths = Directory
            .EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(TemporarySuffix, StringComparison.Ordinal))
            .Select(path => path
                .Substring(_storePath.Length)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/'))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(paths);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Delete(GetFullPath(path));
        }
        catch (DirectoryNotFoundException)
        {
        }

        return Task.CompletedTask;
    }

    public Task<string> BeginUploadAsync(string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(_storePath, StreamingStorage.UploadsFolder));
        using (File.Open(UploadPath(id), FileMode.CreateNew)) { }

        return Task.FromResult(id);
    }

    public async Task<long> AppendAsync(string uploadId, long offset, Stream chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));

        var path = UploadPath(uploadId);
        var tracked = UploadDigestTracker.Shared.Track(uploadId, offset, chunk);
        try
        {
            FileStream file;
            try
            {
                file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, DefaultCopyBufferSize, useAsync: true);
            }
            catch (FileNotFoundException)
            {
                throw new StorageUploadNotFoundException(uploadId);
            }
            catch (DirectoryNotFoundException)
            {
                throw new StorageUploadNotFoundException(uploadId);
            }

            using (file)
            {
                if (file.Length != offset) throw new StorageUploadOffsetException(file.Length, offset);

                file.Seek(0, SeekOrigin.End);
                await tracked.CopyToAsync(file, DefaultCopyBufferSize, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);

                tracked.Complete(file.Length);
                return file.Length;
            }
        }
        catch
        {
            tracked.Fail();
            throw;
        }
    }

    public Task<long?> GetUploadLengthAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Guid.TryParseExact(uploadId, "N", out _)) return Task.FromResult<long?>(null);

        var file = new FileInfo(UploadPath(uploadId));
        return Task.FromResult<long?>(file.Exists ? file.Length : null);
    }

    public async Task<StorageCommitResult> CommitAsync(string uploadId, string path, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        var part = new FileInfo(UploadPath(uploadId));
        if (!part.Exists) throw new StorageUploadNotFoundException(uploadId);

        var length = part.Length;
        var sha = UploadDigestTracker.Shared.TryComplete(uploadId, length);
        if (sha == null)
        {
            using var read = File.Open(part.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            sha = await StreamingStorage.Sha256HexAsync(read, cancellationToken).ConfigureAwait(false);
        }

        if (expectedSha256 != null && !string.Equals(expectedSha256, sha, StringComparison.OrdinalIgnoreCase))
        {
            part.Delete();
            throw new StorageDigestMismatchException(expectedSha256, sha);
        }

        var target = GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        try
        {
            File.Move(part.FullName, target, overwrite: false);
            return new StorageCommitResult(StoragePutResult.Success, length, sha);
        }
        catch (IOException) when (File.Exists(target))
        {
            string existing;
            using (var stored = File.Open(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                existing = await StreamingStorage.Sha256HexAsync(stored, cancellationToken).ConfigureAwait(false);
            }

            part.Delete();
            return new StorageCommitResult(
                string.Equals(existing, sha, StringComparison.Ordinal) ? StoragePutResult.AlreadyExists : StoragePutResult.Conflict,
                length,
                sha);
        }
    }

    public Task AbortAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        UploadDigestTracker.Shared.Forget(uploadId);
        if (!Guid.TryParseExact(uploadId, "N", out _)) return Task.CompletedTask;

        try
        {
            File.Delete(UploadPath(uploadId));
        }
        catch (DirectoryNotFoundException)
        {
        }

        return Task.CompletedTask;
    }

    public Task<StorageBlobInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var file = new FileInfo(GetFullPath(path));
        return Task.FromResult<StorageBlobInfo?>(file.Exists ? new StorageBlobInfo(file.Length, null) : null);
    }

    public Task<Stream?> GetRangeAsync(string path, long offset, long? length, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FileStream file;
        try
        {
            file = new FileStream(GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, DefaultCopyBufferSize, useAsync: true);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            var count = StreamingStorage.ResolveRange(file.Length, offset, length);
            file.Seek(offset, SeekOrigin.Begin);
            return Task.FromResult<Stream?>(new RangeReadStream(file, count));
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    // An id is a GUID in "N" form; anything else could climb out of the uploads folder.
    private string UploadPath(string uploadId)
    {
        if (!Guid.TryParseExact(uploadId, "N", out _)) throw new StorageUploadNotFoundException(uploadId);

        return Path.Combine(_storePath, StreamingStorage.UploadsFolder, uploadId + ".part");
    }

    private string GetFullPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Path is required", nameof(path));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_storePath, path));

        // Verify path is under the _storePath.
        if (!fullPath.StartsWith(_storePath, StringComparison.Ordinal) ||
            fullPath.Length == _storePath.Length)
        {
            throw new ArgumentException("Path resolves outside store path", nameof(path));
        }

        return fullPath;
    }
}