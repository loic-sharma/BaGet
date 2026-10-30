using System;
using Ctx = BaGet.Core.AbstractContext<Microsoft.EntityFrameworkCore.DbContext>;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// <see cref="IMavenRepository"/> over the Maven tables and the storage service. Rows live in the repository of the request
/// (the context's query filter and stamping), files under <c>maven/{group}/{artifact}/{version}/{file}</c> of that repository's
/// storage prefix. Checksums are computed once, on the way in, and kept in the file's row; <c>maven-metadata.xml</c> is never
/// stored.
/// </summary>
public class MavenRepositoryService : IMavenRepository
{
    private readonly IContext _context;
    private readonly IStorageService _storage;
    private readonly IRepositoryContext _repository;
    private readonly IPackageAuthorizationService _authorization;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly ILogger<MavenRepositoryService> _logger;
    private readonly IOptionsSnapshot<MavenOptions>? _options;
    private readonly ArtifactUsageRecorder? _usage;
    private readonly IScanGate? _scanGate;
    private MavenRepositorySettings? _settings;

    /// <param name="context">The database.</param>
    /// <param name="storage">Where files are kept.</param>
    /// <param name="repository">The repository of the request.</param>
    /// <param name="authorization">Decides who may deploy and delete.</param>
    /// <param name="time">The clock.</param>
    /// <param name="gate">The read-only gate, checked before a file is stored or removed.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The <c>Maven:*</c> options.</param>
    /// <param name="usage">Notes downloads for the retention rules; null records nothing.</param>
    /// <param name="scanGate">The vulnerability scanning policy checked before a deploy and told of what was stored.</param>
    public MavenRepositoryService(
        IContext context,
        IStorageService storage,
        IRepositoryContext repository,
        IPackageAuthorizationService authorization,
        SystemTime time,
        MaintenanceGate gate,
        ILogger<MavenRepositoryService> logger,
        IOptionsSnapshot<MavenOptions>? options = null,
        ArtifactUsageRecorder? usage = null,
        IScanGate? scanGate = null)
    {
        _usage = usage;
        _scanGate = scanGate;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options;
    }

    private static readonly KeyedLocks FileLocks = new();

    private long MaxFileBytes => _options?.Value.MaxFileBytes ?? 1024L * 1024 * 1024;

    private string StoragePath(string groupId, string artifactId, string version, string fileName)
        => RepositoryStorage.Scope(_repository.Current, MavenNames.StoragePath(groupId, artifactId, version, fileName));

    /// <inheritdoc/>
    public async Task<MavenRepositorySettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        if (_settings != null) return _settings;

        var key = _repository.Current.Key;
        var json = await _context.Repositories!.AsNoTracking()
            .Where(r => r.Key == key)
            .Select(r => r.Settings)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return _settings = MavenRepositorySettings.Parse(json);
    }

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken)
        => await HasVersionAsync(groupId, artifactId, version, cancellationToken).ConfigureAwait(false) ? ResolutionOutcome.Stored : ResolutionOutcome.NotFound;

    // ---- reads ----------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<MavenCoordinate?> ResolveAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (path.Candidates.Count == 0) return null;
        if (path.Kind == MavenPathKind.File) return path.Candidates[0];

        foreach (var candidate in path.Candidates)
        {
            var artifact = await FindArtifactAsync(candidate.GroupId, candidate.ArtifactId, cancellationToken).ConfigureAwait(false);
            if (artifact == null) continue;

            if (candidate.Version == null) return candidate;

            if (await _context.MavenVersions!.AsNoTracking().AnyAsync(v => v.ArtifactKey == artifact.Key && v.Version == candidate.Version, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        // Nothing stored: the artifact level is the question that gets "not found".
        return path.Candidates[^1];
    }

    private Task<MavenArtifact?> FindArtifactAsync(string groupId, string artifactId, CancellationToken cancellationToken)
        => _context.MavenArtifacts!.AsNoTracking().FirstOrDefaultAsync(a => a.GroupId == groupId && a.ArtifactId == artifactId, cancellationToken);

    /// <inheritdoc/>
    public async Task<MavenDownload?> OpenAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        return path.Kind == MavenPathKind.Metadata
            ? await OpenMetadataAsync(path, cancellationToken).ConfigureAwait(false)
            : await OpenFileAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<MavenDownload?> HeadAsync(MavenRequestPath path, CancellationToken cancellationToken) => OpenAsync(path, cancellationToken);

    private async Task<MavenDownload?> OpenFileAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        var coordinate = path.Candidates[0];
        var artifact = await FindArtifactAsync(coordinate.GroupId, coordinate.ArtifactId, cancellationToken).ConfigureAwait(false);
        if (artifact == null) return null;

        var version = await _context.MavenVersions!.AsNoTracking().FirstOrDefaultAsync(v => v.ArtifactKey == artifact.Key && v.Version == coordinate.Version, cancellationToken).ConfigureAwait(false);
        if (version == null) return null;

        var file = await _context.MavenFiles!.AsNoTracking().FirstOrDefaultAsync(f => f.VersionKey == version.Key && f.FileName == path.BaseName, cancellationToken).ConfigureAwait(false)
                   ?? await NewestSnapshotBuildAsync(version, coordinate, path.BaseName, cancellationToken).ConfigureAwait(false);
        if (file == null) return null;

        if (path.ChecksumAlgorithm != null)
        {
            var hex = HexOf(file, path.ChecksumAlgorithm);
            var bytes = Encoding.ASCII.GetBytes(hex);

            return new MavenDownload(new MemoryStream(bytes, writable: false), bytes.Length, "text/plain", path.FileName, Utc(file.Created));
        }

        var stream = await _storage.GetAsync(StoragePath(coordinate.GroupId, coordinate.ArtifactId, coordinate.Version!, file.FileName!), cancellationToken).ConfigureAwait(false);
        if (stream == null) return null;

        // For the retention rules: the version was just downloaded.
        _usage?.Touch(_repository.Current.Key, MavenNames.ArtifactName(coordinate.GroupId, coordinate.ArtifactId), coordinate.Version);

        return new MavenDownload(stream, file.Size, MavenNames.ContentTypeOf(LastExtension(file.Extension!)), file.FileName!, Utc(file.Created))
        {
            Sha1 = file.Sha1,
            Md5 = file.Md5,
            Sha256 = file.Sha256,
        };
    }

    // "a-1.0-SNAPSHOT.jar" (what a Maven 2 client asks for) is the newest timestamped build of the same classifier and extension.
    private async Task<MavenFile?> NewestSnapshotBuildAsync(MavenVersion version, MavenCoordinate coordinate, string requested, CancellationToken cancellationToken)
    {
        if (!MavenNames.IsSnapshot(version.Version)) return null;

        var parts = MavenNames.ParseFileName(coordinate.ArtifactId, version.Version!, requested);
        if (parts == null || parts.SnapshotTimestamp != null) return null;

        var builds = await _context.MavenFiles!.AsNoTracking()
            .Where(f => f.VersionKey == version.Key && f.SnapshotTimestamp != null && f.Extension == parts.Extension && f.Classifier == parts.Classifier)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return builds.OrderByDescending(f => f.SnapshotTimestamp, StringComparer.Ordinal).ThenByDescending(f => f.SnapshotBuild).FirstOrDefault();
    }

    private async Task<MavenDownload?> OpenMetadataAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        foreach (var candidate in path.Candidates)
        {
            var artifact = await FindArtifactAsync(candidate.GroupId, candidate.ArtifactId, cancellationToken).ConfigureAwait(false);
            if (artifact == null) continue;

            string xml;
            if (candidate.Version == null)
            {
                var versions = await _context.MavenVersions!.AsNoTracking().Where(v => v.ArtifactKey == artifact.Key).Select(v => v.Version!).ToListAsync(cancellationToken).ConfigureAwait(false);
                if (versions.Count == 0) continue;

                xml = MavenMetadata.ForArtifact(candidate.GroupId, candidate.ArtifactId, versions, Utc(artifact.Modified));
            }
            else
            {
                var version = await _context.MavenVersions!.AsNoTracking().FirstOrDefaultAsync(v => v.ArtifactKey == artifact.Key && v.Version == candidate.Version, cancellationToken).ConfigureAwait(false);
                if (version == null) continue;

                var files = await _context.MavenFiles!.AsNoTracking().Where(f => f.VersionKey == version.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
                xml = MavenMetadata.ForVersion(
                    candidate.GroupId,
                    candidate.ArtifactId,
                    candidate.Version,
                    files.Select(f => new MavenFileInfo(f.FileName!, f.Classifier, f.Extension!, f.SnapshotTimestamp, f.SnapshotBuild, Utc(f.Created))),
                    Utc(version.Modified));
            }

            var content = Encoding.UTF8.GetBytes(xml);
            var modified = candidate.Version == null ? artifact.Modified : DateTime.UtcNow;
            if (path.ChecksumAlgorithm != null)
            {
                var hex = Convert.ToHexString(HashOf(path.ChecksumAlgorithm, content)).ToLowerInvariant();
                var text = Encoding.ASCII.GetBytes(hex);

                return new MavenDownload(new MemoryStream(text, writable: false), text.Length, "text/plain", path.FileName, Utc(modified));
            }

            return new MavenDownload(new MemoryStream(content, writable: false), content.Length, "application/xml", path.FileName, Utc(modified))
            {
                Sha1 = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant(),
                Md5 = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant(),
                Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            };
        }

        return null;
    }

    private static byte[] HashOf(string algorithm, byte[] content) => algorithm switch
    {
        "md5" => MD5.HashData(content),
        "sha256" => SHA256.HashData(content),
        "sha512" => SHA512.HashData(content),
        _ => SHA1.HashData(content),
    };

    private static string HexOf(MavenFile file, string algorithm) => algorithm switch
    {
        "md5" => file.Md5!,
        "sha256" => file.Sha256!,
        "sha512" => file.Sha512!,
        _ => file.Sha1!,
    };

    // The extension of "jar.asc" for the content type: the signature is text, the rest follows the last part.
    private static string LastExtension(string extension)
    {
        var dot = extension.LastIndexOf('.');

        return dot < 0 ? extension : extension.Substring(dot + 1);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <inheritdoc/>
    public async Task<MavenSearchPage> SearchAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken)
    {
        IQueryable<MavenArtifact> query = _context.MavenArtifacts!.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim().ToLowerInvariant();
            query = query.Where(a =>
                (a.GroupId!.ToLower() + ":" + a.ArtifactId!.ToLower()).Contains(needle)
                || _context.MavenVersions!.Any(v => v.ArtifactKey == a.Key
                    && ((v.Name != null && v.Name.ToLower().Contains(needle)) || (v.Description != null && v.Description.ToLower().Contains(needle)))));
        }

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 0, 250);

        var all = await query.OrderBy(a => a.GroupId).ThenBy(a => a.ArtifactId)
            .Select(a => new { a.Key, a.GroupId, a.ArtifactId, a.Modified })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Only the artifacts the caller may read count; the names are filtered and paged here.
        var visible = new List<(int Key, string Group, string Artifact, DateTime Modified)>();
        foreach (var row in all)
        {
            if (canRead == null || await canRead(MavenNames.ArtifactName(row.GroupId!, row.ArtifactId!), cancellationToken).ConfigureAwait(false))
            {
                visible.Add((row.Key, row.GroupId!, row.ArtifactId!, row.Modified));
            }
        }

        var page = visible.Skip(skip).Take(take).ToList();
        var keys = page.Select(p => p.Key).ToList();
        var versions = await _context.MavenVersions!.AsNoTracking().Where(v => keys.Contains(v.ArtifactKey)).ToListAsync(cancellationToken).ConfigureAwait(false);

        var hits = new List<MavenSearchHit>();
        foreach (var item in page)
        {
            var own = versions.Where(v => v.ArtifactKey == item.Key).OrderByDescending(v => v.Version, Comparer<string?>.Create(MavenNames.CompareVersions)).ToList();
            var latest = own.FirstOrDefault(v => !v.IsSnapshot) ?? own.FirstOrDefault();

            hits.Add(new MavenSearchHit(item.Group, item.Artifact, latest?.Version, latest?.Name, latest?.Description, Utc(item.Modified)));
        }

        return new MavenSearchPage(visible.Count, hits);
    }

    /// <inheritdoc/>
    public async Task<MavenArtifactInfo?> GetArtifactAsync(string groupId, string artifactId, CancellationToken cancellationToken)
    {
        var artifact = await FindArtifactAsync(groupId, artifactId, cancellationToken).ConfigureAwait(false);
        if (artifact == null) return null;

        var versions = await _context.MavenVersions!.AsNoTracking().Where(v => v.ArtifactKey == artifact.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var keys = versions.Select(v => v.Key).ToList();
        var files = await _context.MavenFiles!.AsNoTracking()
            .Where(f => keys.Contains(f.VersionKey))
            .Select(f => new { f.VersionKey, f.Size, f.SnapshotTimestamp, f.SnapshotBuild })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var summaries = versions
            .OrderByDescending(v => v.Version, Comparer<string?>.Create(MavenNames.CompareVersions))
            .Select(v =>
            {
                var own = files.Where(f => f.VersionKey == v.Key).ToList();

                return new MavenVersionSummary(
                    v.Version!,
                    v.IsSnapshot,
                    v.Name,
                    v.Description,
                    v.Packaging,
                    Utc(v.Created),
                    Utc(v.Modified),
                    own.Count,
                    own.Sum(f => f.Size),
                    own.Where(f => f.SnapshotTimestamp != null).Select(f => (f.SnapshotTimestamp, f.SnapshotBuild)).Distinct().Count());
            })
            .ToList();

        return new MavenArtifactInfo(artifact.GroupId!, artifact.ArtifactId!, Utc(artifact.Created), Utc(artifact.Modified), summaries);
    }

    /// <inheritdoc/>
    public async Task<MavenVersionDetail?> GetVersionAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken)
    {
        var artifact = await FindArtifactAsync(groupId, artifactId, cancellationToken).ConfigureAwait(false);
        if (artifact == null) return null;

        var row = await _context.MavenVersions!.AsNoTracking().FirstOrDefaultAsync(v => v.ArtifactKey == artifact.Key && v.Version == version, cancellationToken).ConfigureAwait(false);
        if (row == null) return null;

        var files = await _context.MavenFiles!.AsNoTracking().Where(f => f.VersionKey == row.Key).OrderBy(f => f.FileName).ToListAsync(cancellationToken).ConfigureAwait(false);

        var summary = new MavenVersionSummary(
            row.Version!,
            row.IsSnapshot,
            row.Name,
            row.Description,
            row.Packaging,
            Utc(row.Created),
            Utc(row.Modified),
            files.Count,
            files.Sum(f => f.Size),
            files.Where(f => f.SnapshotTimestamp != null).Select(f => (f.SnapshotTimestamp, f.SnapshotBuild)).Distinct().Count());

        // The POM shown is the newest build's for a snapshot, the version's own for a release.
        var pomFile = files
            .Where(f => f.Classifier == null && f.Extension == "pom")
            .OrderByDescending(f => f.SnapshotTimestamp, StringComparer.Ordinal).ThenByDescending(f => f.SnapshotBuild)
            .FirstOrDefault();

        MavenPomInfo? pom = null;
        string? pomText = null;
        if (pomFile != null && pomFile.Size <= MavenPom.MaxBytes)
        {
            using var stream = await _storage.GetAsync(StoragePath(groupId, artifactId, version, pomFile.FileName!), cancellationToken).ConfigureAwait(false);
            if (stream != null)
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;
                pomText = new UTF8Encoding(false).GetString(buffer.ToArray());
                buffer.Position = 0;
                pom = MavenPom.TryParse(buffer);
            }
        }

        return new MavenVersionDetail(
            summary,
            files.Select(f => new MavenFileDetail(f.FileName!, f.Classifier, f.Extension!, f.Size, f.Sha1!, f.Md5!, f.Sha256!, f.Sha512!, Utc(f.Created), f.SnapshotTimestamp, f.SnapshotBuild)).ToList(),
            pom,
            pomText);
    }

    // ---- authorization --------------------------------------------------------------------------------------------------

    private async Task<MavenResult?> DenyAsync(BaGetPrincipal? caller, PackageAction action, string name, CancellationToken cancellationToken)
    {
        if (caller == null) return MavenResult.Fail(MavenStatus.Unauthorized, "Authentication is required.");

        if (_repository.Current.Kind != RepositoryKind.Local)
        {
            return MavenResult.Fail(MavenStatus.Forbidden, "This repository takes no changes.");
        }

        if (!await _authorization.AuthorizeAsync(caller, action, name, cancellationToken).ConfigureAwait(false))
        {
            return MavenResult.Fail(MavenStatus.Forbidden, $"You are not allowed to {(action == PackageAction.Push ? "deploy to" : "delete from")} {name}.");
        }

        return null;
    }

    // ---- writes ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The scanning check <see cref="PutAsync"/> would make for this file, without storing anything: a replication that replaces a
    /// stored file asks first, so a refusal cannot cost the copy it was to replace.
    /// </summary>
    /// <exception cref="ScanBlockedException">The scanning policy refuses the file.</exception>
    internal async Task PreflightReplacementAsync(MavenRequestPath path, Stream content, CancellationToken cancellationToken)
    {
        if (path.Kind == MavenPathKind.Metadata || path.ChecksumAlgorithm != null || path.Candidates.Count == 0) return;
        if (_scanGate == null || !_scanGate.Enabled || !await _scanGate.BlocksDeploysAsync(cancellationToken).ConfigureAwait(false)) return;

        var coordinate = path.Candidates[0];
        var parts = MavenNames.ParseFileName(coordinate.ArtifactId, coordinate.Version!, path.BaseName);
        if (parts == null) return;

        MavenPomInfo? pom = null;
        if (parts.Classifier == null && parts.Extension == "pom" && content.Length <= MavenPom.MaxBytes)
        {
            content.Position = 0;
            pom = MavenPom.TryParse(content);
            content.Position = 0;
        }

        await _scanGate.CheckDeployAsync(MavenScanComponents.Of(coordinate.GroupId, coordinate.ArtifactId, coordinate.Version!, pom), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<MavenResult> PutAsync(BaGetPrincipal? caller, MavenRequestPath path, Stream content, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (content == null) throw new ArgumentNullException(nameof(content));

        if (path.Kind == MavenPathKind.Metadata)
        {
            // A client uploads its own maven-metadata.xml (and checksums) after the files. The server generates it from what is
            // stored, so the upload is acknowledged and dropped; it is still a deploy, so the caller must be allowed to deploy.
            MavenResult? denied = null;
            foreach (var candidate in path.Candidates.Select(c => c.Name).Distinct())
            {
                denied = await DenyAsync(caller, PackageAction.Push, candidate, cancellationToken).ConfigureAwait(false);
                if (denied == null) break;
            }

            if (path.Candidates.Count == 0)
            {
                // Group level (plugin prefixes): any deployer may send it; nothing is stored.
                denied = caller == null ? MavenResult.Fail(MavenStatus.Unauthorized, "Authentication is required.") : null;
            }

            return denied ?? MavenResult.Created("maven-metadata.xml is generated by the repository; the upload was ignored.");
        }

        var coordinate = path.Candidates[0];
        var name = coordinate.Name;
        var checkOnly = path.ChecksumAlgorithm != null;

        var refusal = await DenyAsync(caller, PackageAction.Push, name, cancellationToken).ConfigureAwait(false);
        if (refusal != null) return refusal;

        var parts = MavenNames.ParseFileName(coordinate.ArtifactId, coordinate.Version!, path.BaseName);
        if (parts == null)
        {
            return MavenResult.Fail(MavenStatus.Invalid, $"'{path.BaseName}' is not a file of {coordinate.ArtifactId} {coordinate.Version}: expected {coordinate.ArtifactId}-{coordinate.Version}[-classifier].extension.");
        }

        if (checkOnly) return await CheckChecksumAsync(coordinate, path, content, cancellationToken).ConfigureAwait(false);

        return await StoreAsync(caller, coordinate, path.BaseName, parts, content, cancellationToken).ConfigureAwait(false);
    }

    // The client sends "<file>.sha1" after the file; it must say what the server computed. A checksum for a file that is not
    // stored (yet) is acknowledged: the server serves its own.
    private async Task<MavenResult> CheckChecksumAsync(MavenCoordinate coordinate, MavenRequestPath path, Stream content, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = await content.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false)) > 0) read += n;

        var sent = Encoding.ASCII.GetString(buffer, 0, read).Trim();
        var space = sent.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        if (space > 0) sent = sent.Substring(0, space);

        var artifact = await FindArtifactAsync(coordinate.GroupId, coordinate.ArtifactId, cancellationToken).ConfigureAwait(false);
        var version = artifact == null ? null : await _context.MavenVersions!.AsNoTracking().FirstOrDefaultAsync(v => v.ArtifactKey == artifact.Key && v.Version == coordinate.Version, cancellationToken).ConfigureAwait(false);
        var file = version == null ? null : await _context.MavenFiles!.AsNoTracking().FirstOrDefaultAsync(f => f.VersionKey == version.Key && f.FileName == path.BaseName, cancellationToken).ConfigureAwait(false);
        if (file == null) return MavenResult.Created("The file is not stored yet; the repository serves the checksums it computes.");

        if (!string.Equals(sent, HexOf(file, path.ChecksumAlgorithm!), StringComparison.OrdinalIgnoreCase))
        {
            return MavenResult.Fail(MavenStatus.Invalid, $"The {path.ChecksumAlgorithm} checksum does not match the stored file {path.BaseName}.");
        }

        return MavenResult.Created();
    }

    internal sealed class Spooled : IDisposable
    {
        public Spooled(FileStream file, long length, string sha1, string md5, string sha256, string sha512)
        {
            File = file;
            Length = length;
            Sha1 = sha1;
            Md5 = md5;
            Sha256 = sha256;
            Sha512 = sha512;
        }

        public FileStream File { get; }

        public long Length { get; }

        public string Sha1 { get; }

        public string Md5 { get; }

        public string Sha256 { get; }

        public string Sha512 { get; }

        public void Dispose() => File.Dispose();
    }

    // Copies the body to a temporary file (deleted on close) while hashing it; null when it is larger than the limit.
    internal static async Task<Spooled?> SpoolAsync(Stream body, long limit, CancellationToken cancellationToken)
    {
        var file = new FileStream(
            Path.Combine(Path.GetTempPath(), "baget-maven-" + Guid.NewGuid().ToString("N")),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);

        try
        {
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);

            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await body.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (limit > 0 && total > limit)
                {
                    file.Dispose();

                    return null;
                }

                sha1.AppendData(buffer, 0, read);
                md5.AppendData(buffer, 0, read);
                sha256.AppendData(buffer, 0, read);
                sha512.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            file.Position = 0;

            return new Spooled(
                file,
                total,
                Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(sha512.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private async Task<MavenResult> StoreAsync(
        BaGetPrincipal? caller,
        MavenCoordinate coordinate,
        string fileName,
        MavenFileParts parts,
        Stream body,
        CancellationToken cancellationToken)
    {
        var version = coordinate.Version!;
        var name = coordinate.Name;
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (!settings.Accepts(version))
        {
            return MavenResult.Fail(
                MavenStatus.Conflict,
                MavenNames.IsSnapshot(version)
                    ? "This repository takes release versions only; a -SNAPSHOT version was refused."
                    : "This repository takes -SNAPSHOT versions only; a release version was refused.");
        }

        var artifact = await FindArtifactAsync(coordinate.GroupId, coordinate.ArtifactId, cancellationToken).ConfigureAwait(false);
        var existed = false;
        if (artifact == null)
        {
            // Names are case-sensitive, but a file system that is not (Windows, macOS) would keep both spellings in one folder:
            // a name that differs from an existing one only in letter case is refused, whatever the store.
            var group = coordinate.GroupId.ToLowerInvariant();
            var artifactId = coordinate.ArtifactId.ToLowerInvariant();
            if (await _context.MavenArtifacts!.AsNoTracking().AnyAsync(a => a.GroupId!.ToLower() == group && a.ArtifactId!.ToLower() == artifactId, cancellationToken).ConfigureAwait(false))
            {
                return MavenResult.Fail(MavenStatus.Conflict, $"{name} differs only in letter case from an artifact that exists in this repository.");
            }
        }
        else
        {
            var versionKey = await _context.MavenVersions!.AsNoTracking().Where(v => v.ArtifactKey == artifact.Key && v.Version == version).Select(v => (int?)v.Key).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (versionKey == null)
            {
                var lowerVersion = version.ToLowerInvariant();
                if (await _context.MavenVersions!.AsNoTracking().AnyAsync(v => v.ArtifactKey == artifact.Key && v.Version!.ToLower() == lowerVersion, cancellationToken).ConfigureAwait(false))
                {
                    return MavenResult.Fail(MavenStatus.Conflict, $"Version {version} of {name} differs only in letter case from a version that exists.");
                }
            }
            else
            {
                existed = await _context.MavenFiles!.AsNoTracking().AnyAsync(f => f.VersionKey == versionKey && f.FileName == fileName, cancellationToken).ConfigureAwait(false);
                var lowerFile = fileName.ToLowerInvariant();
                if (!existed && await _context.MavenFiles!.AsNoTracking().AnyAsync(f => f.VersionKey == versionKey && f.FileName!.ToLower() == lowerFile, cancellationToken).ConfigureAwait(false))
                {
                    return MavenResult.Fail(MavenStatus.Conflict, $"{fileName} differs only in letter case from a file that exists in {name} {version}.");
                }
            }
        }

        if (existed && !MavenNames.IsSnapshot(version))
        {
            if (!settings.AllowRedeploy)
            {
                return MavenResult.Fail(MavenStatus.Conflict, $"{fileName} already exists in {name} {version}, and this repository does not allow a release to be deployed again.");
            }

            // Replacing a released file is a deletion of the old one as well (Artifactory's Delete/Overwrite).
            var overwrite = await DenyAsync(caller, PackageAction.Manage, name, cancellationToken).ConfigureAwait(false);
            if (overwrite != null) return overwrite;
        }

        _gate.ThrowIfClosed();

        var limit = MaxFileBytes;
        using var spooled = await SpoolAsync(body, limit, cancellationToken).ConfigureAwait(false);
        if (spooled == null) return MavenResult.Fail(MavenStatus.TooLarge, $"The file is larger than {limit} bytes.");

        MavenPomInfo? pom = null;
        if (parts.Classifier == null && parts.Extension == "pom" && spooled.Length <= MavenPom.MaxBytes)
        {
            pom = MavenPom.TryParse(spooled.File);
            spooled.File.Position = 0;
        }

        // The scanning policy of the repository may refuse an artifact with known vulnerabilities (ScanBlockedException, 403): a
        // POM brings its dependencies, any other file the artifact itself.
        if (_scanGate != null && _scanGate.Enabled && await _scanGate.BlocksDeploysAsync(cancellationToken).ConfigureAwait(false))
        {
            await _scanGate.CheckDeployAsync(MavenScanComponents.Of(coordinate.GroupId, coordinate.ArtifactId, version, pom), cancellationToken).ConfigureAwait(false);
        }

        var path = StoragePath(coordinate.GroupId, coordinate.ArtifactId, version, fileName);

        // One deploy of a file at a time in this process: a deploy that finds a file at the path cannot tell a leftover from a
        // concurrent deploy's file whose row is not written yet, and replacing the latter would leave its row with no file (or the
        // loser, refused afterwards, would delete the only copy). Under the lock the row decides, so a loser is refused untouched.
        // Deploys on other nodes sharing the store are not fenced (docs/maven-repositories.md).
        using var deployLock = await FileLocks.LockAsync(path, cancellationToken).ConfigureAwait(false);
        if (!existed && !MavenNames.IsSnapshot(version))
        {
            var rowNow = await (from f in _context.MavenFiles!.AsNoTracking()
                                join v in _context.MavenVersions!.AsNoTracking() on f.VersionKey equals v.Key
                                join a in _context.MavenArtifacts!.AsNoTracking() on v.ArtifactKey equals a.Key
                                where f.FileName == fileName && v.Version == version && a.GroupId == coordinate.GroupId && a.ArtifactId == coordinate.ArtifactId
                                select f.Key).AnyAsync(cancellationToken).ConfigureAwait(false);
            if (rowNow)
            {
                if (!settings.AllowRedeploy)
                {
                    return MavenResult.Fail(MavenStatus.Conflict, $"{fileName} already exists in {name} {version}, and this repository does not allow a release to be deployed again.");
                }

                var overwriteNow = await DenyAsync(caller, PackageAction.Manage, name, cancellationToken).ConfigureAwait(false);
                if (overwriteNow != null) return overwriteNow;
                existed = true;
            }
        }

        var put = await _storage.PutAsync(path, spooled.File, MavenNames.ContentTypeOf(LastExtension(parts.Extension)), cancellationToken).ConfigureAwait(false);
        if (put == StoragePutResult.Conflict)
        {
            // An earlier file at the path (a replaced release, or a leftover of an attempt that never got its row).
            await _storage.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
            spooled.File.Position = 0;
            put = await _storage.PutAsync(path, spooled.File, MavenNames.ContentTypeOf(LastExtension(parts.Extension)), cancellationToken).ConfigureAwait(false);
        }

        var storedByThisCall = put == StoragePutResult.Success;
        var now = _time.UtcNow;
        var isNewArtifact = false;
        MavenVersion? versionRow = null;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                var row = await _context.MavenArtifacts!.FirstOrDefaultAsync(a => a.GroupId == coordinate.GroupId && a.ArtifactId == coordinate.ArtifactId, cancellationToken).ConfigureAwait(false);
                isNewArtifact = row == null;
                if (row == null)
                {
                    row = new MavenArtifact { GroupId = coordinate.GroupId, ArtifactId = coordinate.ArtifactId, Created = now, Modified = now };
                    _context.MavenArtifacts!.Add(row);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                versionRow = await _context.MavenVersions!.FirstOrDefaultAsync(v => v.ArtifactKey == row.Key && v.Version == version, cancellationToken).ConfigureAwait(false);
                if (versionRow == null)
                {
                    versionRow = new MavenVersion { ArtifactKey = row.Key, Version = version, IsSnapshot = MavenNames.IsSnapshot(version), Created = now, Modified = now };
                    _context.MavenVersions!.Add(versionRow);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                var file = await _context.MavenFiles!.FirstOrDefaultAsync(f => f.VersionKey == versionRow.Key && f.FileName == fileName, cancellationToken).ConfigureAwait(false);
                if (file != null && !MavenNames.IsSnapshot(version) && !settings.AllowRedeploy)
                {
                    // A concurrent deploy of the same release file won.
                    if (storedByThisCall && !existed) await TryDeleteAsync(path).ConfigureAwait(false);

                    return MavenResult.Fail(MavenStatus.Conflict, $"{fileName} already exists in {name} {version}.");
                }

                if (file == null)
                {
                    file = new MavenFile { VersionKey = versionRow.Key, FileName = fileName };
                    _context.MavenFiles!.Add(file);
                }

                file.Classifier = parts.Classifier;
                file.Extension = parts.Extension;
                file.SnapshotTimestamp = parts.SnapshotTimestamp;
                file.SnapshotBuild = parts.SnapshotBuild;
                file.Size = spooled.Length;
                file.Sha1 = spooled.Sha1;
                file.Md5 = spooled.Md5;
                file.Sha256 = spooled.Sha256;
                file.Sha512 = spooled.Sha512;
                file.Created = now;

                if (pom != null)
                {
                    versionRow.Packaging = Cut(pom.Packaging, Ctx.MaxMavenPackagingLength);
                    versionRow.Name = Cut(pom.Name, Ctx.MaxMavenNameLength);
                    versionRow.Description = Cut(pom.Description, Ctx.MaxMavenDescriptionLength);
                }

                versionRow.Modified = now;
                row.Modified = now;
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e) && attempt < 2)
            {
                // A concurrent deploy created the artifact or version first: the rolled-back attempt left tracked entities
                // that no longer exist, so look again and add to the winner's rows.
                (_context as DbContext)?.ChangeTracker.Clear();
            }
            catch
            {
                if (storedByThisCall && !existed) await TryDeleteAsync(path).ConfigureAwait(false);

                throw;
            }
        }

        if (isNewArtifact) await _authorization.OnPushedAsync(caller!, name, isNewId: true, cancellationToken).ConfigureAwait(false);

        if (parts.SnapshotTimestamp != null && settings.MaxSnapshotBuilds > 0)
        {
            await PruneSnapshotsAsync(coordinate, versionRow!.Key, settings.MaxSnapshotBuilds, cancellationToken).ConfigureAwait(false);
        }

        if (_scanGate != null) await _scanGate.RegisterStoredAsync(name, version, cancellationToken, contentChanged: IsScannedFile(parts)).ConfigureAwait(false);

        return MavenResult.Created($"Deployed {fileName}.", name);
    }

    /// <summary>The scan reads only the main POM, so any other file of the version says nothing new to the scanner.</summary>
    private static bool IsScannedFile(MavenFileParts parts) => parts.Classifier == null && parts.Extension == "pom";

    private static string? Cut(string? value, int length)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value.Substring(0, length);

    private async Task TryDeleteAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync(path, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Could not delete the Maven file {Path}", path);
        }
    }

    /// <inheritdoc/>
    public async Task<MavenResult> DeployAsync(BaGetPrincipal? caller, MavenDeployment deployment, CancellationToken cancellationToken)
    {
        if (deployment == null) throw new ArgumentNullException(nameof(deployment));

        var groupId = deployment.GroupId?.Trim() ?? string.Empty;
        var artifactId = deployment.ArtifactId?.Trim() ?? string.Empty;
        var version = deployment.Version?.Trim() ?? string.Empty;
        if (!MavenNames.IsValidGroupId(groupId)) return MavenResult.Fail(MavenStatus.Invalid, "The group id is not valid: use letters, digits, '_' and '-' in dot-separated parts.");
        if (!MavenNames.IsValidArtifactId(artifactId)) return MavenResult.Fail(MavenStatus.Invalid, "The artifact id is not valid.");
        if (!MavenNames.IsValidVersion(version)) return MavenResult.Fail(MavenStatus.Invalid, "The version is not valid.");
        if (deployment.File == null && deployment.Pom == null) return MavenResult.Fail(MavenStatus.Invalid, "Choose a file or a POM to deploy.");

        var packaging = string.IsNullOrWhiteSpace(deployment.Packaging) ? "jar" : deployment.Packaging.Trim();
        var extension = string.IsNullOrWhiteSpace(deployment.Extension) ? packaging : deployment.Extension.Trim().TrimStart('.');
        var classifier = string.IsNullOrWhiteSpace(deployment.Classifier) ? null : deployment.Classifier.Trim();
        var coordinate = new MavenCoordinate(groupId, artifactId, version);

        // A snapshot is stored as a timestamped build, as 'mvn deploy' stores it; the build number follows the stored ones.
        string stem;
        if (MavenNames.IsSnapshot(version))
        {
            var build = 1;
            var artifact = await FindArtifactAsync(groupId, artifactId, cancellationToken).ConfigureAwait(false);
            var versionKey = artifact == null ? null : await _context.MavenVersions!.AsNoTracking().Where(v => v.ArtifactKey == artifact.Key && v.Version == version).Select(v => (int?)v.Key).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (versionKey != null)
            {
                build = (await _context.MavenFiles!.AsNoTracking().Where(f => f.VersionKey == versionKey).Select(f => (int?)f.SnapshotBuild).MaxAsync(cancellationToken).ConfigureAwait(false) ?? 0) + 1;
            }

            stem = $"{version.Substring(0, version.Length - MavenNames.SnapshotSuffix.Length)}-{_time.UtcNow.ToString("yyyyMMdd.HHmmss", CultureInfo.InvariantCulture)}-{build.ToString(CultureInfo.InvariantCulture)}";
        }
        else
        {
            stem = version;
        }

        var files = new List<(string Name, Stream Content)>();
        MemoryStream? generated = null;
        try
        {
            if (deployment.File != null) files.Add(($"{artifactId}-{stem}{(classifier == null ? string.Empty : "-" + classifier)}.{extension}", deployment.File));

            if (deployment.Pom != null)
            {
                files.Add(($"{artifactId}-{stem}.pom", deployment.Pom));
            }
            else if (classifier == null && !(extension == "pom" && deployment.File != null))
            {
                generated = new MemoryStream(new UTF8Encoding(false).GetBytes(MavenPom.Generate(groupId, artifactId, version, packaging)));
                files.Add(($"{artifactId}-{stem}.pom", generated));
            }

            MavenResult last = MavenResult.Fail(MavenStatus.Invalid, "Nothing to deploy.");
            foreach (var (fileName, content) in files)
            {
                var parts = MavenNames.ParseFileName(artifactId, version, fileName);
                if (parts == null) return MavenResult.Fail(MavenStatus.Invalid, $"'{fileName}' is not a valid Maven file name: check the classifier and the extension.");

                var denied = await DenyAsync(caller, PackageAction.Push, coordinate.Name, cancellationToken).ConfigureAwait(false);
                if (denied != null) return denied;

                last = await StoreAsync(caller, coordinate, fileName, parts, content, cancellationToken).ConfigureAwait(false);
                if (!last.Succeeded) return last;
            }

            return MavenResult.Created($"Deployed {coordinate.Name} {version}.", coordinate.Name);
        }
        finally
        {
            generated?.Dispose();
        }
    }

    // ---- cache support (RemoteMavenRepository) ----------------------------------------------------------------------------

    /// <summary>
    /// Keeps a file a remote repository fetched from its upstream: the bytes under <c>maven/…</c> and the artifact, version and file
    /// rows, exactly as a deployed file is kept (so counting, search, the pages, access and the database copy need nothing new).
    /// No caller, policy or ownership applies: the upstream decides what exists. A file that is already stored (a concurrent request
    /// won) is left as it is. Refused while the maintenance gate is closed, before anything is written.
    /// </summary>
    internal async Task StoreCachedAsync(MavenCoordinate coordinate, string fileName, MavenFileParts parts, Spooled spooled, CancellationToken cancellationToken)
    {
        _gate.ThrowIfClosed();

        var version = coordinate.Version!;
        MavenPomInfo? pom = null;
        if (parts.Classifier == null && parts.Extension == "pom" && spooled.Length <= MavenPom.MaxBytes)
        {
            pom = MavenPom.TryParse(spooled.File);
        }

        spooled.File.Position = 0;

        var path = StoragePath(coordinate.GroupId, coordinate.ArtifactId, version, fileName);
        var contentType = MavenNames.ContentTypeOf(LastExtension(parts.Extension));
        var put = await _storage.PutAsync(path, spooled.File, contentType, cancellationToken).ConfigureAwait(false);
        if (put == StoragePutResult.Conflict)
        {
            // A leftover of an attempt that never got its row.
            await _storage.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
            spooled.File.Position = 0;
            put = await _storage.PutAsync(path, spooled.File, contentType, cancellationToken).ConfigureAwait(false);
        }

        var storedByThisCall = put == StoragePutResult.Success;
        var now = _time.UtcNow;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                var artifact = await _context.MavenArtifacts!.FirstOrDefaultAsync(a => a.GroupId == coordinate.GroupId && a.ArtifactId == coordinate.ArtifactId, cancellationToken).ConfigureAwait(false);
                if (artifact == null)
                {
                    artifact = new MavenArtifact { GroupId = coordinate.GroupId, ArtifactId = coordinate.ArtifactId, Created = now, Modified = now };
                    _context.MavenArtifacts!.Add(artifact);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                var versionRow = await _context.MavenVersions!.FirstOrDefaultAsync(v => v.ArtifactKey == artifact.Key && v.Version == version, cancellationToken).ConfigureAwait(false);
                if (versionRow == null)
                {
                    versionRow = new MavenVersion { ArtifactKey = artifact.Key, Version = version, IsSnapshot = MavenNames.IsSnapshot(version), Created = now, Modified = now };
                    _context.MavenVersions!.Add(versionRow);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                if (await _context.MavenFiles!.AnyAsync(f => f.VersionKey == versionRow.Key && f.FileName == fileName, cancellationToken).ConfigureAwait(false))
                {
                    // A concurrent request stored the same file first; its bytes are the upstream's too.
                    return;
                }

                _context.MavenFiles!.Add(new MavenFile
                {
                    VersionKey = versionRow.Key,
                    FileName = fileName,
                    Classifier = parts.Classifier,
                    Extension = parts.Extension,
                    SnapshotTimestamp = parts.SnapshotTimestamp,
                    SnapshotBuild = parts.SnapshotBuild,
                    Size = spooled.Length,
                    Sha1 = spooled.Sha1,
                    Md5 = spooled.Md5,
                    Sha256 = spooled.Sha256,
                    Sha512 = spooled.Sha512,
                    Created = now,
                });

                if (pom != null)
                {
                    versionRow.Packaging = Cut(pom.Packaging, Ctx.MaxMavenPackagingLength);
                    versionRow.Name = Cut(pom.Name, Ctx.MaxMavenNameLength);
                    versionRow.Description = Cut(pom.Description, Ctx.MaxMavenDescriptionLength);
                }

                versionRow.Modified = now;
                artifact.Modified = now;
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                if (_scanGate != null) await _scanGate.RegisterStoredAsync(coordinate.Name, version, cancellationToken, contentChanged: IsScannedFile(parts)).ConfigureAwait(false);

                return;
            }
            catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e) && attempt < 2)
            {
                (_context as DbContext)?.ChangeTracker.Clear();
            }
            catch
            {
                if (storedByThisCall) await TryDeleteAsync(path).ConfigureAwait(false);

                throw;
            }
        }
    }

    /// <summary>
    /// Whether the repository stores the version (some file of it), without asking any upstream: what the resolution preview of a
    /// virtual repository shows for a member another member already shadows.
    /// </summary>
    internal async Task<bool> HasVersionAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken)
    {
        var artifact = await FindArtifactAsync(groupId, artifactId, cancellationToken).ConfigureAwait(false);

        return artifact != null
            && await _context.MavenVersions!.AsNoTracking().AnyAsync(v => v.ArtifactKey == artifact.Key && v.Version == version, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes what a remote repository cached, rows and files together: everything, one artifact (<paramref name="artifactName"/>
    /// <c>group:artifact</c>), or one version of it. No caller check: the administration that asks has done it. Closed gate: refused.
    /// </summary>
    /// <returns>The number of files deleted.</returns>
    internal async Task<int> PurgeAsync(string? artifactName, string? version, CancellationToken cancellationToken)
    {
        _gate.ThrowIfClosed();

        var artifacts = _context.MavenArtifacts!.AsQueryable();
        if (artifactName != null)
        {
            if (!MavenNames.TryParseArtifactName(artifactName, out var group, out var artifactId)) return 0;

            artifacts = artifacts.Where(a => a.GroupId == group && a.ArtifactId == artifactId);
        }

        var deleted = 0;
        while (true)
        {
            var batch = await artifacts.OrderBy(a => a.Key).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0) return deleted;

            foreach (var artifact in batch)
            {
                var versions = _context.MavenVersions!.Where(v => v.ArtifactKey == artifact.Key);
                if (version != null) versions = versions.Where(v => v.Version == version);

                var rows = await versions.ToListAsync(cancellationToken).ConfigureAwait(false);
                var keys = rows.Select(v => v.Key).ToList();
                var files = await _context.MavenFiles!.Where(f => keys.Contains(f.VersionKey)).Select(f => new { f.VersionKey, f.FileName }).ToListAsync(cancellationToken).ConfigureAwait(false);

                _context.MavenVersions!.RemoveRange(rows);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (!await _context.MavenVersions!.AnyAsync(v => v.ArtifactKey == artifact.Key, cancellationToken).ConfigureAwait(false))
                {
                    _context.MavenArtifacts!.Remove(artifact);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                foreach (var file in files)
                {
                    var owner = rows.First(v => v.Key == file.VersionKey).Version!;
                    await TryDeleteAsync(StoragePath(artifact.GroupId!, artifact.ArtifactId!, owner, file.FileName!)).ConfigureAwait(false);
                }

                deleted += files.Count;
            }

            // One artifact at most is filtered by name, and a version filter only exists with one: a single pass.
            if (artifactName != null) return deleted;
        }
    }

    // ---- deletes --------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<MavenResult> DeleteFileAsync(BaGetPrincipal? caller, MavenCoordinate version, string fileName, CancellationToken cancellationToken)
    {
        if (version?.Version == null) throw new ArgumentException("A version is required.", nameof(version));

        var denied = await DenyAsync(caller, PackageAction.Manage, version.Name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        var artifact = await _context.MavenArtifacts!.FirstOrDefaultAsync(a => a.GroupId == version.GroupId && a.ArtifactId == version.ArtifactId, cancellationToken).ConfigureAwait(false);
        var row = artifact == null ? null : await _context.MavenVersions!.FirstOrDefaultAsync(v => v.ArtifactKey == artifact.Key && v.Version == version.Version, cancellationToken).ConfigureAwait(false);
        var file = row == null ? null : await _context.MavenFiles!.FirstOrDefaultAsync(f => f.VersionKey == row.Key && f.FileName == fileName, cancellationToken).ConfigureAwait(false);
        if (artifact == null || row == null || file == null) return MavenResult.Fail(MavenStatus.NotFound, $"There is no file {fileName} in {version.Name} {version.Version}.");

        _gate.ThrowIfClosed();

        _context.MavenFiles!.Remove(file);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await RemoveIfEmptyAsync(artifact, row, cancellationToken).ConfigureAwait(false);
        await TryDeleteAsync(StoragePath(version.GroupId, version.ArtifactId, version.Version, fileName)).ConfigureAwait(false);

        return MavenResult.Ok($"Deleted {fileName}.", 1, version.Name);
    }

    // The version goes with its last file (when one is given), the artifact with its last version.
    private async Task RemoveIfEmptyAsync(MavenArtifact artifact, MavenVersion? version, CancellationToken cancellationToken)
    {
        if (version != null)
        {
            if (!await _context.MavenFiles!.AnyAsync(f => f.VersionKey == version.Key, cancellationToken).ConfigureAwait(false))
            {
                _context.MavenVersions!.Remove(version);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                version.Modified = _time.UtcNow;
            }
        }

        if (!await _context.MavenVersions!.AnyAsync(v => v.ArtifactKey == artifact.Key, cancellationToken).ConfigureAwait(false))
        {
            _context.MavenArtifacts!.Remove(artifact);
        }
        else
        {
            artifact.Modified = _time.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<MavenResult> DeleteVersionAsync(BaGetPrincipal? caller, MavenCoordinate version, CancellationToken cancellationToken)
    {
        if (version?.Version == null) throw new ArgumentException("A version is required.", nameof(version));

        var denied = await DenyAsync(caller, PackageAction.Manage, version.Name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        var artifact = await _context.MavenArtifacts!.FirstOrDefaultAsync(a => a.GroupId == version.GroupId && a.ArtifactId == version.ArtifactId, cancellationToken).ConfigureAwait(false);
        var row = artifact == null ? null : await _context.MavenVersions!.FirstOrDefaultAsync(v => v.ArtifactKey == artifact.Key && v.Version == version.Version, cancellationToken).ConfigureAwait(false);
        if (artifact == null || row == null) return MavenResult.Fail(MavenStatus.NotFound, $"There is no version {version.Version} of {version.Name}.");

        _gate.ThrowIfClosed();

        var names = await _context.MavenFiles!.Where(f => f.VersionKey == row.Key).Select(f => f.FileName!).ToListAsync(cancellationToken).ConfigureAwait(false);
        _context.MavenVersions!.Remove(row);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The version and its files are gone; the artifact goes with its last version.
        await RemoveIfEmptyAsync(artifact, version: null, cancellationToken).ConfigureAwait(false);
        foreach (var name in names) await TryDeleteAsync(StoragePath(version.GroupId, version.ArtifactId, version.Version, name)).ConfigureAwait(false);

        return MavenResult.Ok($"Deleted {version.Name} {version.Version}.", names.Count, version.Name);
    }

    /// <inheritdoc/>
    public async Task<MavenResult> DeleteArtifactAsync(BaGetPrincipal? caller, string groupId, string artifactId, CancellationToken cancellationToken)
    {
        var name = MavenNames.ArtifactName(groupId, artifactId);
        var denied = await DenyAsync(caller, PackageAction.Manage, name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        var artifact = await _context.MavenArtifacts!.FirstOrDefaultAsync(a => a.GroupId == groupId && a.ArtifactId == artifactId, cancellationToken).ConfigureAwait(false);
        if (artifact == null) return MavenResult.Fail(MavenStatus.NotFound, $"There is no artifact {name}.");

        _gate.ThrowIfClosed();

        var versions = await _context.MavenVersions!.Where(v => v.ArtifactKey == artifact.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var keys = versions.Select(v => v.Key).ToList();
        var files = await _context.MavenFiles!.Where(f => keys.Contains(f.VersionKey)).Select(f => new { f.VersionKey, f.FileName }).ToListAsync(cancellationToken).ConfigureAwait(false);

        _context.MavenArtifacts!.Remove(artifact);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var file in files)
        {
            var version = versions.First(v => v.Key == file.VersionKey).Version!;
            await TryDeleteAsync(StoragePath(groupId, artifactId, version, file.FileName!)).ConfigureAwait(false);
        }

        return MavenResult.Ok($"Deleted {name}.", files.Count, name);
    }

    /// <inheritdoc/>
    public async Task<MavenResult> CleanupSnapshotsAsync(BaGetPrincipal? caller, MavenCoordinate version, int keep, CancellationToken cancellationToken)
    {
        if (version?.Version == null) throw new ArgumentException("A version is required.", nameof(version));
        if (keep < 1) return MavenResult.Fail(MavenStatus.Invalid, "Keep at least one build.");

        var denied = await DenyAsync(caller, PackageAction.Manage, version.Name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        if (!MavenNames.IsSnapshot(version.Version)) return MavenResult.Fail(MavenStatus.Invalid, $"{version.Version} is not a snapshot version.");

        var artifact = await FindArtifactAsync(version.GroupId, version.ArtifactId, cancellationToken).ConfigureAwait(false);
        var row = artifact == null ? null : await _context.MavenVersions!.AsNoTracking().FirstOrDefaultAsync(v => v.ArtifactKey == artifact.Key && v.Version == version.Version, cancellationToken).ConfigureAwait(false);
        if (row == null) return MavenResult.Fail(MavenStatus.NotFound, $"There is no version {version.Version} of {version.Name}.");

        _gate.ThrowIfClosed();

        var removed = await PruneSnapshotsAsync(version, row.Key, keep, cancellationToken).ConfigureAwait(false);

        return MavenResult.Ok($"Deleted {removed} file{(removed == 1 ? string.Empty : "s")} of older builds.", removed, version.Name);
    }

    // Keeps the newest 'keep' timestamped builds (every file of a build goes together) and deletes the rest.
    private async Task<int> PruneSnapshotsAsync(MavenCoordinate version, int versionKey, int keep, CancellationToken cancellationToken)
    {
        var builds = await _context.MavenFiles!.Where(f => f.VersionKey == versionKey && f.SnapshotTimestamp != null).ToListAsync(cancellationToken).ConfigureAwait(false);
        var old = builds
            .GroupBy(f => (f.SnapshotTimestamp, f.SnapshotBuild))
            .OrderByDescending(g => g.Key.SnapshotTimestamp, StringComparer.Ordinal).ThenByDescending(g => g.Key.SnapshotBuild)
            .Skip(keep)
            .SelectMany(g => g)
            .ToList();
        if (old.Count == 0) return 0;

        _context.MavenFiles!.RemoveRange(old);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var file in old) await TryDeleteAsync(StoragePath(version.GroupId, version.ArtifactId, version.Version!, file.FileName!)).ConfigureAwait(false);

        return old.Count;
    }
}
