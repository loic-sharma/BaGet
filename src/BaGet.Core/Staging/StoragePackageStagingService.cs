using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// Stages uploads in <see cref="IStorageService"/> under a prefix of their own, so that
/// the two requests of a browser upload may land on different instances and so that
/// every storage provider works, exactly as the validation pipeline stages pushed
/// packages under "validation/".
/// </summary>
/// <remarks>
/// Nothing serves the staging prefix: no route turns a request into a storage path, so a
/// staged upload is reachable only through <see cref="OpenAsync"/>, which is where the
/// owner is checked.
/// </remarks>
public class StoragePackageStagingService : IPackageStagingService
{
    /// <summary>
    /// The prefix every staged upload lives under. A token never becomes anything else.
    /// </summary>
    internal const string StagingPathPrefix = "staging";

    /// <summary>
    /// 128 bits from a cryptographic generator, hex encoded. The token is the only thing
    /// naming a staged upload, so guessing one must not be feasible.
    /// </summary>
    private const int TokenBytes = 16;

    private const int TokenLength = TokenBytes * 2;

    private readonly IStorageService _storage;
    private readonly SystemTime _time;
    private readonly ILogger<StoragePackageStagingService> _logger;
    private readonly IRepositoryContext? _repository;

    /// <summary>
    /// The repository uploads are staged for: the default one stages under <c>staging/</c>, any other under
    /// <c>repos/{name}/staging/</c>, so a token staged for one repository cannot be opened from another and
    /// <see cref="SweepAsync"/> collects the current repository's uploads only. Null means the default repository.
    /// </summary>
    public StoragePackageStagingService(
        IStorageService storage,
        SystemTime time,
        ILogger<StoragePackageStagingService> logger,
        IRepositoryContext? repository = null)
    {
        _repository = repository;
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private RepositoryInfo Repository => _repository?.Current ?? RepositoryInfo.Default;

    /// <summary>
    /// The staging prefix of the current repository: <c>staging</c>, or <c>repos/{name}/staging</c>.
    /// </summary>
    private string Prefix => RepositoryStorage.Scope(Repository, StagingPathPrefix);

    internal static string StagedPath(string? token) => StagedPath(RepositoryInfo.Default, token);

    internal static string StagedPath(RepositoryInfo repository, string? token)
        => RepositoryStorage.Scope(repository, $"{StagingPathPrefix}/{token}.nupkg");

    internal static string MetadataPath(string? token) => MetadataPath(RepositoryInfo.Default, token);

    internal static string MetadataPath(RepositoryInfo repository, string? token)
        => RepositoryStorage.Scope(repository, $"{StagingPathPrefix}/{token}.json");

    public async Task<string> StageAsync(Stream? package, string? ownerId, CancellationToken cancellationToken = default)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));

        var token = CreateToken();
        var metadata = new StagedUpload
        {
            OwnerId = Owner(ownerId),
            StagedAtUtc = _time.UtcNow,
        };

        // The metadata is written before the package because the sweep finds abandoned
        // uploads through the metadata: a package whose metadata never arrived would be
        // collected by nothing.
        using (var json = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(metadata)))
        {
            await StoreAsync(MetadataPath(Repository, token), json, "application/json", cancellationToken);
        }

        try
        {
            await StoreAsync(StagedPath(Repository, token), package, "application/octet-stream", cancellationToken);
        }
        catch
        {
            // Half a staged upload can never be confirmed, so it is removed now rather
            // than left for the sweep.
            await ForgetAsync(token, CancellationToken.None);
            throw;
        }

        // The token itself only ever reaches Debug. It is the whole capability over a
        // staged upload, and a log at Information is on by default and usually shipped
        // to a store read by far more people than the uploader; the correlation id is
        // enough to follow one upload through the log without handing it over.
        _logger.LogDebug("Staged an upload as {Token}", token);
        _logger.LogInformation("Staged an upload, correlation {Correlation}", Correlation(token));

        return token;
    }

    public async Task<Stream?> OpenAsync(string? token, string? ownerId, CancellationToken cancellationToken = default)
    {
        if (!IsToken(token)) return null;

        var read = await ReadMetadataAsync(token, cancellationToken);
        if (read.Metadata == null) return null;

        // The real authorization: the prefix being unreachable over HTTP is a property of
        // the route table, not an access check, and two users share one store.
        if (!IsOwner(read.Metadata, ownerId))
        {
            _logger.LogWarning(
                "Refusing staged upload {Correlation}: it was staged by a different user",
                Correlation(token));

            return null;
        }

        return await OpenOrNullAsync(StagedPath(Repository, token), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string? token, string? ownerId, CancellationToken cancellationToken = default)
    {
        if (!IsToken(token)) return false;

        // The same owner check <see cref="OpenAsync"/> makes. Removal is not a read, but
        // it destroys an upload its owner may be about to confirm, so a token someone
        // else learned must not be enough to discard it.
        var read = await ReadMetadataAsync(token, cancellationToken);
        if (read.Metadata == null || !IsOwner(read.Metadata, ownerId))
        {
            return false;
        }

        await ForgetAsync(token, cancellationToken);

        return true;
    }

    public async Task<int> SweepAsync(DateTime stagedBefore, CancellationToken cancellationToken = default)
    {
        if (!(_storage is IListableStorageService listable))
        {
            // Said out loud: silence here would mean staged uploads accumulating
            // unnoticed on a store that cannot be swept.
            _logger.LogWarning(
                "Cannot sweep abandoned staged uploads: {Storage} cannot list what it holds",
                _storage.GetType().Name);

            return 0;
        }

        var paths = await listable.ListAsync(Prefix, cancellationToken);
        var stored = new HashSet<string>(paths, StringComparer.Ordinal);

        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var token = TokenOrNull(path);
            if (token != null) tokens.Add(token);
        }

        var swept = 0;
        foreach (var token in tokens)
        {
            if (!await IsCollectableAsync(token, stagedBefore, stored, cancellationToken)) continue;

            await ForgetAsync(token, cancellationToken);
            swept++;
        }

        if (swept > 0)
        {
            _logger.LogInformation("Swept {Count} staged uploads abandoned before {Cutoff}", swept, stagedBefore);
        }

        return swept;
    }

    /// <summary>
    /// Whether the sweep may discard an upload. An upload is collected when it is older
    /// than the cutoff, when its metadata is gone - nobody can open it then, since the
    /// owner it belonged to is unknowable - or when its metadata will never parse. It is
    /// kept when the metadata is there but could not be read this time: a store that is
    /// briefly unreachable, or a file a backup or a scanner is holding, says nothing
    /// about whether the upload was abandoned, and collecting it then would discard an
    /// upload staged a minute ago whose preview is still open.
    /// </summary>
    private async Task<bool> IsCollectableAsync(
        string? token,
        DateTime stagedBefore,
        HashSet<string> stored,
        CancellationToken cancellationToken)
    {
        var read = await ReadMetadataAsync(token, cancellationToken);

        switch (read.Status)
        {
            case StagedMetadataStatus.Readable:
                return read.Metadata?.StagedAtUtc < stagedBefore;

            case StagedMetadataStatus.Unreadable:
                return true;

            default:
                if (!stored.Contains(MetadataPath(Repository, token))) return true;

                _logger.LogWarning(
                    "Keeping staged upload {Correlation}: its metadata could not be read this time",
                    Correlation(token));

                return false;
        }
    }

    private async Task StoreAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var result = await _storage.PutAsync(path, content, contentType, cancellationToken);

        if (result == StoragePutResult.Conflict)
        {
            throw new InvalidOperationException($"Could not stage an upload at '{path}'; the path is already in use");
        }
    }

    /// <summary>
    /// Remove both of an upload's blobs, whoever owns it. Every caller that reaches here
    /// has already decided it may.
    /// </summary>
    private async Task ForgetAsync(string? token, CancellationToken cancellationToken)
    {
        await DeleteAsync(StagedPath(Repository, token), cancellationToken);
        await DeleteAsync(MetadataPath(Repository, token), cancellationToken);
    }

    private async Task<StagedMetadataRead> ReadMetadataAsync(string? token, CancellationToken cancellationToken)
    {
        using var stream = await OpenOrNullAsync(MetadataPath(Repository, token), cancellationToken);
        if (stream == null) return new StagedMetadataRead(StagedMetadataStatus.Unavailable, null);

        try
        {
            var metadata = await JsonSerializer.DeserializeAsync<StagedUpload>(stream, cancellationToken: cancellationToken);

            return metadata == null
                ? new StagedMetadataRead(StagedMetadataStatus.Unreadable, null)
                : new StagedMetadataRead(StagedMetadataStatus.Readable, metadata);
        }
        catch (JsonException e)
        {
            // Content that does not parse will not parse on the next pass either, unlike
            // a file that could not be opened this time.
            _logger.LogWarning(e, "Staged upload {Correlation} has unreadable metadata", Correlation(token));

            return new StagedMetadataRead(StagedMetadataStatus.Unreadable, null);
        }
    }

    private async Task<Stream?> OpenOrNullAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // IStorageService is documented to answer a missing path with null, but no
            // implementation does: the file store throws, each cloud store throws its
            // own type, and the null store really does return null. An upload that
            // cannot be read back is an unknown token either way.
            return await _storage.GetAsync(path, cancellationToken);
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            _logger.LogDebug(e, "Nothing staged at {Path}", path);
            return null;
        }
    }

    private async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            // Left for the next sweep rather than failing a confirmed upload.
            _logger.LogWarning(e, "Could not remove staged content under {Prefix}", StagingPathPrefix);
        }
    }

    private static string CreateToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes)).ToLowerInvariant();

    /// <summary>
    /// A short, non-reversible name for an upload, so a log can follow one upload without
    /// carrying the token that would let its reader act on it.
    /// </summary>
    #pragma warning disable CS8604
    private static string Correlation(string? token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)), 0, 4).ToLowerInvariant();
    #pragma warning restore CS8604

    private static bool IsOwner(StagedUpload metadata, string? ownerId)
        => string.Equals(metadata.OwnerId, Owner(ownerId), StringComparison.Ordinal);

    /// <summary>
    /// A token becomes a storage path, so anything that is not one is refused before it
    /// gets there - a caller-supplied "../packages/..." must never be opened or deleted.
    /// </summary>
    private static bool IsToken(string? token)
    {
        if (token == null || token.Length != TokenLength) return false;

        foreach (var character in token)
        {
            var isHex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
            if (!isHex) return false;
        }

        return true;
    }

    /// <summary>
    /// The token a staged path belongs to, or null if the path is not one of ours.
    /// </summary>
    private string? TokenOrNull(string? path)
    {
        if (path == null) return null;
        if (!path.StartsWith(Prefix + "/", StringComparison.Ordinal)) return null;

        var extension = Path.GetExtension(path);
        if (extension != ".json" && extension != ".nupkg") return null;

        var token = Path.GetFileNameWithoutExtension(path);

        return IsToken(token) ? token : null;
    }

    /// <summary>
    /// With accounts disabled there is no user to own an upload, so every upload shares
    /// the one empty owner - the same identity the API key path has there.
    /// </summary>
    private static string Owner(string? ownerId) => ownerId ?? string.Empty;

    /// <summary>
    /// Why a metadata read did not produce metadata. "Not there" and "not readable right
    /// now" are different facts, and the sweep must not treat the second as the first.
    /// </summary>
    private enum StagedMetadataStatus
    {
        Readable,

        /// <summary>
        /// The metadata could not be opened. It may be absent, or the store may be
        /// unreachable or the file held open by something else.
        /// </summary>
        Unavailable,

        /// <summary>
        /// The metadata was read and is not metadata, so it will never become readable.
        /// </summary>
        Unreadable,
    }

    private readonly struct StagedMetadataRead
    {
        public StagedMetadataRead(StagedMetadataStatus status, StagedUpload? metadata)
        {
            Status = status;
            Metadata = metadata;
        }

        public StagedMetadataStatus Status { get; }

        public StagedUpload? Metadata { get; }
    }

    internal class StagedUpload
    {
        public string? OwnerId { get; set; }

        public DateTime StagedAtUtc { get; set; }
    }
}