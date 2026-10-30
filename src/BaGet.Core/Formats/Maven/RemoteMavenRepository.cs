using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// <see cref="IMavenRepository"/> of a <see cref="RepositoryKind.Remote"/> Maven repository: a caching proxy of an upstream
/// repository (Maven Central, <c>https://repo.maven.apache.org/maven2/</c>; another BaGet, Artifactory, Nexus...).
/// </summary>
/// <remarks>
/// <para><b>Files.</b> A stored file is served from storage, checksums from its row. Otherwise it is downloaded, hashed while it is
/// written to a temporary file, compared with the checksum the upstream publishes beside it (<c>.sha1</c>, else <c>.sha256</c>, else
/// <c>.md5</c>; a mismatch is neither stored nor served: <see cref="MavenUpstreamException"/>, HTTP 502) and, with
/// <see cref="RemoteRepositorySettings.StoreLocally"/>, kept in the repository's storage with its artifact, version and file rows, so
/// it is served locally from then on, also while the upstream is down. Without it the download is streamed through and nothing is
/// kept. Concurrent requests for the same file download it once. A <c>-SNAPSHOT</c> file under its plain name
/// (<c>lib-1.0-SNAPSHOT.jar</c>) moves, so it is always streamed from the upstream and never kept (the timestamped build it names is,
/// when asked for by that name); a stored build only answers it while the upstream cannot.</para>
/// <para><b>Metadata and checksums of metadata</b> (<c>maven-metadata.xml</c>, artifact, version and group level, so plugin prefixes
/// resolve too) come from the upstream (<see cref="MavenUpstreamClient"/>: cached for the metadata period, 404s for the negative
/// period, an expired answer when the upstream is down, nothing in offline mode). When the upstream cannot answer, the metadata
/// generated from the versions this repository stored answers instead; when neither can, the upstream's failure is raised (HTTP 502).</para>
/// <para><b>Writes</b> are refused (403): a remote repository takes no changes. <b>Pages and search</b> cover the stored artifacts only.</para>
/// </remarks>
public sealed class RemoteMavenRepository : IMavenRepository
{
    private const string NoChanges = "This repository caches an upstream repository and takes no changes.";

    private static readonly KeyedLocks Downloads = new();

    private readonly MavenRepositoryService _local;
    private readonly MavenUpstreamClient _upstream;
    private readonly ILogger<RemoteMavenRepository> _logger;
    private readonly IOptionsSnapshot<MavenOptions>? _options;

    public RemoteMavenRepository(MavenRepositoryService local, MavenUpstreamClient upstream, ILogger<RemoteMavenRepository> logger, IOptionsSnapshot<MavenOptions>? options = null)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options;
    }

    // ---- reads ----------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// A file names its artifact. Metadata one folder deeper than the group (<c>a/b/c/maven-metadata.xml</c>) is the artifact
    /// <c>b:c</c> or version <c>c</c> of <c>a:b</c>; the upstream's own document says which (its <c>groupId</c> and
    /// <c>artifactId</c>), else what is stored decides, else the artifact level.
    /// </remarks>
    public async Task<MavenCoordinate?> ResolveAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (path.Candidates.Count == 0) return null;
        if (path.Kind == MavenPathKind.File) return path.Candidates[0];

        // A single candidate (a path two folders deep) can still be a group's plugin list, which names no artifact.
        var single = path.Candidates.Count == 1;
        if (single && (path.GroupOnly == null || path.Candidates[0].Version != null)) return path.Candidates[0];

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream != null)
        {
            var answer = await _upstream.GetMetadataAsync(upstream, path.Plain.RelativePath, cancellationToken).ConfigureAwait(false);
            if (answer.Status == MavenUpstreamStatus.Found)
            {
                if (Identify(answer.Content!, path.Candidates) is { } match) return match;

                // Not about the artifact: a group-level document is judged by the repository's Read, not by an unrelated artifact's.
                if (single && IsGroupLevel(answer.Content!)) return null;
            }
        }

        if (single) return path.Candidates[0];

        return await _local.ResolveAsync(path, cancellationToken).ConfigureAwait(false);
    }

    // A group-level document: a plugin list with no artifactId of its own.
    internal static bool IsGroupLevel(byte[] metadata)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(metadata), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var root = XDocument.Load(reader).Root;

            return root?.Element("plugins") != null && string.IsNullOrWhiteSpace(root.Element("artifactId")?.Value);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    // The candidate a metadata document is about: the artifact its groupId and artifactId name; the version level when it names a version.
    internal static MavenCoordinate? Identify(byte[] metadata, System.Collections.Generic.IReadOnlyList<MavenCoordinate> candidates)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(metadata), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var root = XDocument.Load(reader).Root;
            var group = root?.Element("groupId")?.Value.Trim();
            var artifact = root?.Element("artifactId")?.Value.Trim();
            var version = root?.Element("version")?.Value.Trim();

            return candidates.FirstOrDefault(c => c.GroupId == group && c.ArtifactId == artifact && (c.Version == null) == string.IsNullOrEmpty(version));
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public Task<MavenDownload?> OpenAsync(MavenRequestPath path, CancellationToken cancellationToken) => OpenCoreAsync(path, head: false, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenDownload?> HeadAsync(MavenRequestPath path, CancellationToken cancellationToken) => OpenCoreAsync(path, head: true, cancellationToken);

    private async Task<MavenDownload?> OpenCoreAsync(MavenRequestPath path, bool head, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);

        return path.Kind == MavenPathKind.Metadata
            ? await OpenMetadataAsync(upstream, path, cancellationToken).ConfigureAwait(false)
            : await OpenFileAsync(upstream, path, head, cancellationToken).ConfigureAwait(false);
    }

    // ---- metadata -------------------------------------------------------------------------------------------------------

    private async Task<MavenDownload?> OpenMetadataAsync(ResolvedUpstream? upstream, MavenRequestPath path, CancellationToken cancellationToken)
    {
        if (upstream == null) return await _local.OpenAsync(path, cancellationToken).ConfigureAwait(false);

        var plain = path.Plain;
        var answer = await _upstream.GetMetadataAsync(upstream, plain.RelativePath, cancellationToken).ConfigureAwait(false);
        if (answer.Status == MavenUpstreamStatus.Found)
        {
            var content = answer.Content!;
            if (path.ChecksumAlgorithm != null)
            {
                var text = Encoding.ASCII.GetBytes(Hex(path.ChecksumAlgorithm, content));

                return new MavenDownload(new MemoryStream(text, writable: false), text.Length, "text/plain", path.FileName, answer.Fetched);
            }

            return new MavenDownload(new MemoryStream(content, writable: false), content.Length, "application/xml", path.FileName, answer.Fetched)
            {
                Sha1 = Hex("sha1", content),
                Md5 = Hex("md5", content),
                Sha256 = Hex("sha256", content),
            };
        }

        // The upstream has nothing to say (404, offline, down): the versions this repository stored still answer.
        var stored = await _local.OpenAsync(path, cancellationToken).ConfigureAwait(false);
        if (stored != null || answer.Status != MavenUpstreamStatus.Unavailable) return stored;

        throw new MavenUpstreamException(answer.Message ?? "The upstream repository could not be reached.");
    }

    internal static string Hex(string algorithm, byte[] content)
    {
        var hash = algorithm switch
        {
            "md5" => System.Security.Cryptography.MD5.HashData(content),
            "sha256" => System.Security.Cryptography.SHA256.HashData(content),
            "sha512" => System.Security.Cryptography.SHA512.HashData(content),
            _ => System.Security.Cryptography.SHA1.HashData(content),
        };

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ---- files ----------------------------------------------------------------------------------------------------------

    private async Task<MavenDownload?> OpenFileAsync(ResolvedUpstream? upstream, MavenRequestPath path, bool head, CancellationToken cancellationToken)
    {
        var coordinate = path.Candidates[0];
        var parts = MavenNames.ParseFileName(coordinate.ArtifactId, coordinate.Version!, path.BaseName);

        // A snapshot under its plain name moves: the upstream answers first, and a stored build only when it cannot.
        var moving = MavenNames.IsSnapshot(coordinate.Version) && (parts == null || parts.SnapshotTimestamp == null);
        if (!moving)
        {
            var stored = await _local.OpenAsync(path, cancellationToken).ConfigureAwait(false);
            if (stored != null) return stored;
        }

        if (upstream == null || upstream.Settings.Offline) return moving ? await _local.OpenAsync(path, cancellationToken).ConfigureAwait(false) : null;

        try
        {
            var fetched = path.ChecksumAlgorithm != null
                ? await ProxyChecksumAsync(upstream, path, cancellationToken).ConfigureAwait(false)
                : await FetchFileAsync(upstream, path, coordinate, parts, moving, head, cancellationToken).ConfigureAwait(false);
            if (fetched != null || !moving) return fetched;
        }
        catch (MavenUpstreamException) when (moving)
        {
            // Falls through to a stored build.
        }

        return await _local.OpenAsync(path, cancellationToken).ConfigureAwait(false);
    }

    // The upstream's checksum of a file this repository does not store (or of a moving snapshot): small, remembered like metadata.
    private async Task<MavenDownload?> ProxyChecksumAsync(ResolvedUpstream upstream, MavenRequestPath path, CancellationToken cancellationToken)
    {
        var answer = await _upstream.GetChecksumAsync(upstream, path.RelativePath, cancellationToken).ConfigureAwait(false);
        switch (answer.Status)
        {
            case MavenUpstreamStatus.Found:
                return new MavenDownload(new MemoryStream(answer.Content!, writable: false), answer.Content!.Length, "text/plain", path.FileName, answer.Fetched);
            case MavenUpstreamStatus.Unavailable:
                throw new MavenUpstreamException(answer.Message ?? "The upstream repository could not be reached.");
            default:
                return null;
        }
    }

    private async Task<MavenDownload?> FetchFileAsync(
        ResolvedUpstream upstream,
        MavenRequestPath path,
        MavenCoordinate coordinate,
        MavenFileParts? parts,
        bool moving,
        bool head,
        CancellationToken cancellationToken)
    {
        var relative = path.RelativePath;
        if (_upstream.IsKnownAbsent(upstream, relative)) return null;

        var cacheable = upstream.Settings.StoreLocally && parts != null && !moving;
        if (head)
        {
            // Only the headers are asked; nothing is stored for a HEAD.
            using var probe = await _upstream.DownloadAsync(upstream, relative, headOnly: true, cancellationToken).ConfigureAwait(false);

            return probe == null ? null : Streamed(probe, path, Stream.Null);
        }

        if (!cacheable)
        {
            var passthrough = await _upstream.DownloadAsync(upstream, relative, headOnly: false, cancellationToken).ConfigureAwait(false);

            return passthrough == null ? null : Streamed(passthrough, path, new DisposingStream(passthrough));
        }

        // One download per file at a time: the second request finds the first one's stored copy.
        using var turn = await Downloads.LockAsync($"{upstream.Key}|{relative}", cancellationToken).ConfigureAwait(false);

        var already = await _local.OpenAsync(path, cancellationToken).ConfigureAwait(false);
        if (already != null) return already;

        using var activity = BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.MirrorFetch);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageId, coordinate.Name);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageVersion, coordinate.Version);

        try
        {
            using var download = await _upstream.DownloadAsync(upstream, relative, headOnly: false, cancellationToken).ConfigureAwait(false);
            if (download == null)
            {
                activity?.SetTag(BaGetDiagnostics.Tags.Result, "NotFound");

                return null;
            }

            var limit = _options?.Value.MaxFileBytes ?? 1024L * 1024 * 1024;
            if (limit > 0 && download.Length > limit)
            {
                throw new MavenUpstreamException(_upstream.Rejected(upstream, $"{path.FileName} is larger than {limit} bytes."));
            }

            MavenRepositoryService.Spooled? spooled;
            try
            {
                spooled = await MavenRepositoryService.SpoolAsync(download.Content, limit, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException)
            {
                throw new MavenUpstreamException(_upstream.Rejected(upstream, "The download from the upstream was cut off."));
            }

            if (spooled == null) throw new MavenUpstreamException(_upstream.Rejected(upstream, $"{path.FileName} is larger than {limit} bytes."));

            using (spooled)
            {
                // What arrived must be what the upstream's own checksum promises; a mismatch is never stored or served.
                await VerifyAsync(upstream, path, spooled, cancellationToken).ConfigureAwait(false);

                await _local.StoreCachedAsync(coordinate, path.BaseName, parts!, spooled, cancellationToken).ConfigureAwait(false);
            }

            activity?.SetTag(BaGetDiagnostics.Tags.Result, "Success");
        }
        catch (Exception e) when (e is not MaintenanceException and not OperationCanceledException)
        {
            BaGetDiagnostics.Fail(activity, e);

            throw;
        }

        return await _local.OpenAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyAsync(ResolvedUpstream upstream, MavenRequestPath path, MavenRepositoryService.Spooled spooled, CancellationToken cancellationToken)
    {
        foreach (var (algorithm, actual) in new[] { ("sha1", spooled.Sha1), ("sha256", spooled.Sha256), ("md5", spooled.Md5) })
        {
            var answer = await _upstream.GetChecksumAsync(upstream, path.RelativePath + "." + algorithm, cancellationToken).ConfigureAwait(false);
            if (answer.Status == MavenUpstreamStatus.Found)
            {
                if (!string.Equals(Encoding.ASCII.GetString(answer.Content!), actual, StringComparison.OrdinalIgnoreCase))
                {
                    throw new MavenUpstreamException(_upstream.Rejected(upstream, $"{path.FileName} does not match the {algorithm} checksum the upstream publishes."));
                }

                return;
            }

            // The first checksum the upstream publishes decides; one it cannot be asked for leaves the file unverified.
            if (answer.Status != MavenUpstreamStatus.NotFound)
            {
                _logger.LogDebug("No checksum of {File} could be fetched from the upstream of repository {Repository}; it is stored unverified.", path.FileName, upstream.Name);

                return;
            }
        }
    }

    private static MavenDownload Streamed(MavenUpstreamDownload download, MavenRequestPath path, Stream content)
    {
        var extension = path.BaseName.Substring(path.BaseName.LastIndexOf('.') + 1);

        return new MavenDownload(content, download.Length ?? 0, MavenNames.ContentTypeOf(extension), path.FileName, download.LastModified ?? DateTime.UtcNow);
    }

    // Disposing the stream releases the download (its connection) too.
    private sealed class DisposingStream : Stream
    {
        private readonly MavenUpstreamDownload _download;

        public DisposingStream(MavenUpstreamDownload download)
        {
            _download = download;
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

        public override int Read(byte[] buffer, int offset, int count) => _download.Content.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _download.Content.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _download.Dispose();

            base.Dispose(disposing);
        }
    }

    // ---- listings and pages: what was stored ----------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>Only the artifacts this repository stored: a repository's whole catalogue is not searched through a cache.</remarks>
    public Task<MavenSearchPage> SearchAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken)
        => _local.SearchAsync(text, skip, take, canRead, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenArtifactInfo?> GetArtifactAsync(string groupId, string artifactId, CancellationToken cancellationToken)
        => _local.GetArtifactAsync(groupId, artifactId, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenVersionDetail?> GetVersionAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken)
        => _local.GetVersionAsync(groupId, artifactId, version, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenRepositorySettings> GetSettingsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new MavenRepositorySettings { Policy = MavenPolicy.Both });

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken)
    {
        if (await _local.HasVersionAsync(groupId, artifactId, version, cancellationToken).ConfigureAwait(false)) return ResolutionOutcome.Stored;

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return ResolutionOutcome.NotFound;

        // The same gate a download applies, so the preview cannot disagree with a real request.
        if (upstream.Settings.Offline) return ResolutionOutcome.Offline;

        var folder = MavenNames.GroupPath(groupId) + "/" + artifactId;
        var answer = await _upstream.GetMetadataAsync(upstream, folder + "/" + MavenNames.MetadataFileName, cancellationToken).ConfigureAwait(false);
        if (answer.Status == MavenUpstreamStatus.Found && Lists(answer.Content!, version)) return ResolutionOutcome.WouldFetch;
        if (answer.Status == MavenUpstreamStatus.Unavailable) return ResolutionOutcome.Unavailable;

        // A repository without artifact-level metadata still has the POM of a version it holds.
        var pom = $"{folder}/{version}/{artifactId}-{version}.pom";
        if (_upstream.IsKnownAbsent(upstream, pom)) return ResolutionOutcome.NotFound;

        try
        {
            using var found = await _upstream.DownloadAsync(upstream, pom, headOnly: true, cancellationToken).ConfigureAwait(false);

            return found == null ? ResolutionOutcome.NotFound : ResolutionOutcome.WouldFetch;
        }
        catch (MavenUpstreamException)
        {
            return ResolutionOutcome.Unavailable;
        }
    }

    private static bool Lists(byte[] metadata, string version)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(metadata), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            return XDocument.Load(reader).Root?.Element("versioning")?.Element("versions")?.Elements("version").Any(v => v.Value.Trim() == version) == true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    // ---- writes: none ---------------------------------------------------------------------------------------------------

    private static Task<MavenResult> Refuse(BaGetPrincipal? caller)
        => Task.FromResult(caller == null ? MavenResult.Fail(MavenStatus.Unauthorized, "Authentication is required.") : MavenResult.Fail(MavenStatus.Forbidden, NoChanges));

    /// <inheritdoc/>
    public Task<MavenResult> PutAsync(BaGetPrincipal? caller, MavenRequestPath path, Stream content, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<MavenResult> DeployAsync(BaGetPrincipal? caller, MavenDeployment deployment, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteFileAsync(BaGetPrincipal? caller, MavenCoordinate version, string fileName, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteVersionAsync(BaGetPrincipal? caller, MavenCoordinate version, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteArtifactAsync(BaGetPrincipal? caller, string groupId, string artifactId, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<MavenResult> CleanupSnapshotsAsync(BaGetPrincipal? caller, MavenCoordinate version, int keep, CancellationToken cancellationToken) => Refuse(caller);
}
