using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// <see cref="INpmRegistry"/> of a <see cref="RepositoryKind.Remote"/> npm repository: a caching proxy of an upstream registry
/// (<c>https://registry.npmjs.org/</c>, another BaGet, Artifactory, Verdaccio...).
/// </summary>
/// <remarks>
/// <para><b>Metadata.</b> Packuments come from the upstream (<see cref="NpmUpstreamClient"/>: cached for the metadata period,
/// 404s for the negative period, an expired answer when the upstream is down, nothing in offline mode) with every
/// <c>dist.tarball</c> rewritten to this server, so clients download through the repository. When the upstream cannot answer
/// (down, offline, or it no longer has the package) the versions this repository stored answer instead; when neither can, the
/// upstream's failure is raised (<see cref="NpmUpstreamException"/>, HTTP 502).</para>
/// <para><b>Tarballs.</b> A stored version is served from storage. Otherwise it is downloaded, hashed while it is written to a
/// temporary file, checked against the upstream's own <c>integrity</c> / <c>shasum</c>, and (with
/// <see cref="RemoteRepositorySettings.StoreLocally"/>) kept in the repository's storage with its package and version rows, so
/// it is served locally from then on, also while the upstream is down. Without it the download is streamed through and nothing is
/// kept. Concurrent requests for the same version download it once.</para>
/// <para><b>Writes</b> are refused (403): a remote repository takes no changes. <b>Search</b> covers the stored packages only.</para>
/// </remarks>
public sealed class RemoteNpmRegistry : INpmRegistry
{
    private const string NoChanges = "This repository caches an upstream registry and takes no changes.";

    private static readonly KeyedLocks Downloads = new();

    private readonly NpmRegistryService _local;
    private readonly NpmUpstreamClient _upstream;
    private readonly IUrlGenerator _urls;
    private readonly ILogger<RemoteNpmRegistry> _logger;

    public RemoteNpmRegistry(NpmRegistryService local, NpmUpstreamClient upstream, IUrlGenerator urls, ILogger<RemoteNpmRegistry> logger)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ---- reads ----------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<JsonObject?> GetPackumentAsync(string name, bool abbreviated, CancellationToken cancellationToken)
    {
        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return await _local.GetPackumentAsync(name, abbreviated, cancellationToken).ConfigureAwait(false);

        var answer = await _upstream.GetPackumentAsync(upstream, name, abbreviated, cancellationToken).ConfigureAwait(false);
        if (answer.Status == NpmUpstreamStatus.Found)
        {
            return Rewrite(answer.Document!, name);
        }

        // The upstream has nothing to say (404, offline, down): what this repository stored still answers.
        var stored = await _local.GetPackumentAsync(name, abbreviated, cancellationToken).ConfigureAwait(false);
        if (stored != null || answer.Status != NpmUpstreamStatus.Unavailable) return stored;

        throw new NpmUpstreamException(answer.Message ?? "The upstream registry could not be reached.");
    }

    /// <inheritdoc/>
    public async Task<NpmPackageInfo?> GetPackageAsync(string name, CancellationToken cancellationToken)
    {
        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream != null)
        {
            var answer = await _upstream.GetPackumentAsync(upstream, name, abbreviated: false, cancellationToken).ConfigureAwait(false);
            if (answer.Status == NpmUpstreamStatus.Found) return ToInfo(answer.Document!, name);

            var stored = await _local.GetPackageAsync(name, cancellationToken).ConfigureAwait(false);
            if (stored == null && answer.Status == NpmUpstreamStatus.Unavailable)
            {
                throw new NpmUpstreamException(answer.Message ?? "The upstream registry could not be reached.");
            }

            return stored;
        }

        return await _local.GetPackageAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>?> GetDistTagsAsync(string name, CancellationToken cancellationToken)
        => (await GetPackageAsync(name, cancellationToken).ConfigureAwait(false))?.DistTags;

    /// <inheritdoc/>
    /// <remarks>Only the packages this repository stored: a registry's whole catalogue is not searched through a cache.</remarks>
    public Task<NpmSearchPage> SearchAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead = null, CancellationToken cancellationToken = default)
        => _local.SearchAsync(text, skip, take, canRead, cancellationToken);

    /// <inheritdoc/>
    public async Task<NpmTarball?> OpenTarballAsync(string name, string file, CancellationToken cancellationToken)
    {
        if (!NpmNames.TryParseTarballFile(name, file, out var version)) return null;

        var stored = await _local.OpenTarballAsync(name, file, cancellationToken).ConfigureAwait(false);
        if (stored != null) return stored;

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null || upstream.Settings.Offline || _upstream.IsKnownAbsent(upstream, name, version)) return null;

        // One download per version at a time: the second request finds the first one's stored copy.
        using var turn = await Downloads.LockAsync($"{upstream.Key}|{name}|{NpmNames.Comparable(version)}", cancellationToken).ConfigureAwait(false);

        stored = await _local.OpenTarballAsync(name, file, cancellationToken).ConfigureAwait(false);
        if (stored != null) return stored;

        var answer = await _upstream.GetPackumentAsync(upstream, name, abbreviated: true, cancellationToken).ConfigureAwait(false);
        if (answer.Status == NpmUpstreamStatus.Unavailable) throw new NpmUpstreamException(answer.Message ?? "The upstream registry could not be reached.");
        if (answer.Status != NpmUpstreamStatus.Found) return null;

        var document = answer.Document!;
        var manifest = FindVersion(document, version, out var upstreamVersion);
        if (manifest == null) return null;
        // Stored under the upstream's spelling only: a file name with other build metadata (or case) is a different URL, not a second copy.
        if (!string.Equals(upstreamVersion, version, StringComparison.Ordinal)) return null;

        var dist = manifest["dist"] as JsonObject;
        var announcedIntegrity = Text(dist?["integrity"]);
        var announcedShasum = Text(dist?["shasum"]);
        var url = NpmUpstreamClient.TarballUri(upstream.Settings, name, upstreamVersion!, Text(dist?["tarball"]));

        using var activity = BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.MirrorFetch);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageId, name);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageVersion, version);

        try
        {
            var download = await _upstream.DownloadAsync(upstream, name, version, url, cancellationToken).ConfigureAwait(false);
            if (download == null)
            {
                activity?.SetTag(BaGetDiagnostics.Tags.Result, "NotFound");
                _logger.LogWarning("The upstream of repository {Repository} lists {Package}@{Version} but has no tarball for it.", upstream.Name, name, version);

                return null;
            }

            if (!upstream.Settings.StoreLocally)
            {
                // Pass-through: the client verifies the integrity it has from the packument.
                activity?.SetTag(BaGetDiagnostics.Tags.Result, "PassThrough");

                return new NpmTarball(new OwningStream(download), download.Length ?? 0, announcedIntegrity ?? string.Empty);
            }

            using (download)
            {
                await using var temporary = new FileStream(
                    Path.Combine(Path.GetTempPath(), "baget-npm-" + Guid.NewGuid().ToString("N")),
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81920,
                    FileOptions.DeleteOnClose | FileOptions.Asynchronous);

                long length;
                string integrity, shasum;
                try
                {
                    (length, integrity, shasum) = await CopyAndHashAsync(download.Content, temporary, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException)
                {
                    throw new NpmUpstreamException(_upstream.Rejected(upstream, "The download from the upstream was cut off."));
                }

                // What arrived must be what the upstream's own packument promised; a mismatch is never stored or served.
                if ((announcedIntegrity != null && announcedIntegrity.StartsWith("sha512-", StringComparison.Ordinal) && !string.Equals(announcedIntegrity, integrity, StringComparison.Ordinal))
                    || (announcedShasum != null && !string.Equals(announcedShasum, shasum, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new NpmUpstreamException(_upstream.Rejected(upstream, $"The tarball of {name}@{version} does not match the integrity its packument announces."));
                }

                var time = document["time"] as JsonObject;
                var published = ParseTime(Text(time?[upstreamVersion!])) ?? DateTime.UtcNow;
                var created = ParseTime(Text(time?["created"])) ?? published;
                var modified = ParseTime(Text(document["modified"])) ?? ParseTime(Text(time?["modified"])) ?? published;

                await _local.StoreCachedAsync(
                    new NpmCachedVersion(name, version, manifest, published, created, modified, Text(manifest["deprecated"]), integrity, shasum, length, Tags(document)),
                    temporary,
                    cancellationToken).ConfigureAwait(false);
            }

            activity?.SetTag(BaGetDiagnostics.Tags.Result, "Success");
        }
        catch (Exception e) when (e is not MaintenanceException and not OperationCanceledException)
        {
            BaGetDiagnostics.Fail(activity, e);

            throw;
        }

        return await _local.OpenTarballAsync(name, file, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string name, string version, CancellationToken cancellationToken)
    {
        if (await _local.ProbeAsync(name, version, cancellationToken).ConfigureAwait(false) == ResolutionOutcome.Stored) return ResolutionOutcome.Stored;

        var upstream = await _upstream.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return ResolutionOutcome.NotFound;

        // The same gate a download applies, so the preview cannot disagree with a real request.
        if (upstream.Settings.Offline) return ResolutionOutcome.Offline;
        if (_upstream.IsKnownAbsent(upstream, name, version)) return ResolutionOutcome.NotFound;

        var answer = await _upstream.GetPackumentAsync(upstream, name, abbreviated: true, cancellationToken).ConfigureAwait(false);

        return answer.Status switch
        {
            NpmUpstreamStatus.Found => FindVersion(answer.Document!, version, out _) == null ? ResolutionOutcome.NotFound : ResolutionOutcome.WouldFetch,
            NpmUpstreamStatus.Unavailable => ResolutionOutcome.Unavailable,
            NpmUpstreamStatus.Offline => ResolutionOutcome.Offline,
            _ => ResolutionOutcome.NotFound,
        };
    }

    // ---- writes: none ---------------------------------------------------------------------------------------------------

    private static Task<NpmResult> Refuse(BaGetPrincipal? caller)
        => Task.FromResult(caller == null ? NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.") : NpmResult.Fail(NpmStatus.Forbidden, NoChanges));

    /// <inheritdoc/>
    public Task<NpmResult> PublishAsync(BaGetPrincipal? caller, NpmPublication publication, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<NpmResult> PublishTarballAsync(BaGetPrincipal? caller, Stream tarball, string tag, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<NpmResult> SetDistTagAsync(BaGetPrincipal? caller, string name, string tag, string version, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<NpmResult> RemoveDistTagAsync(BaGetPrincipal? caller, string name, string tag, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<NpmResult> DeprecateAsync(BaGetPrincipal? caller, string name, IReadOnlyDictionary<string, string?> messages, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<NpmResult> UnpublishVersionsAsync(BaGetPrincipal? caller, string name, IReadOnlyCollection<string> versions, IReadOnlyDictionary<string, string>? newTags, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<NpmResult> UnpublishPackageAsync(BaGetPrincipal? caller, string name, CancellationToken cancellationToken) => Refuse(caller);

    /// <inheritdoc/>
    public Task<NpmResult> DeleteTarballAsync(BaGetPrincipal? caller, string name, string file, CancellationToken cancellationToken) => Refuse(caller);

    // ---- documents ------------------------------------------------------------------------------------------------------

    // Points every tarball at this server. Versions that are not semantic versions are dropped (this server could not serve
    // their tarball names, and npm cannot install them from a registry that does not either).
    private JsonObject Rewrite(JsonObject document, string name)
    {
        var registry = _urls.GetNpmRegistryUrl();
        document["name"] = name;

        if (document["versions"] is JsonObject versions)
        {
            foreach (var pair in versions.ToList())
            {
                if (pair.Value is not JsonObject manifest || !NpmNames.TryParseVersion(pair.Key, out _))
                {
                    versions.Remove(pair.Key);
                    (document["time"] as JsonObject)?.Remove(pair.Key);

                    continue;
                }

                var dist = manifest["dist"] as JsonObject ?? new JsonObject();
                dist["tarball"] = $"{registry}{name}/-/{NpmNames.TarballFileName(name, pair.Key)}";
                manifest["dist"] = dist;
            }

            if (document["dist-tags"] is JsonObject tags)
            {
                foreach (var tag in tags.ToList())
                {
                    if (tag.Value is not JsonValue v || !v.TryGetValue<string>(out var target) || !versions.ContainsKey(target)) tags.Remove(tag.Key);
                }
            }
        }

        return document;
    }

    private static NpmPackageInfo ToInfo(JsonObject document, string name)
    {
        var time = document["time"] as JsonObject;
        var versions = new List<NpmVersionInfo>();
        foreach (var pair in (document["versions"] as JsonObject) ?? new JsonObject())
        {
            if (pair.Value is not JsonObject manifest || !NpmNames.TryParseVersion(pair.Key, out _)) continue;

            var copy = (JsonObject)manifest.DeepClone();
            var dist = copy["dist"] as JsonObject;
            var integrity = Text(dist?["integrity"]) ?? string.Empty;
            var shasum = Text(dist?["shasum"]) ?? string.Empty;
            dist?.Remove("tarball");

            versions.Add(new NpmVersionInfo(
                pair.Key,
                ParseTime(Text(time?[pair.Key])) ?? DateTime.UnixEpoch,
                Text(copy["deprecated"]),
                dist?["unpackedSize"] is JsonValue size && size.TryGetValue<long>(out var bytes) ? bytes : 0,
                integrity,
                shasum,
                copy));
        }

        var ordered = versions.OrderBy(v => v.Version, new NpmVersionOrder()).ToList();
        var created = ParseTime(Text(time?["created"])) ?? ordered.FirstOrDefault()?.Published ?? DateTime.UnixEpoch;
        var modified = ParseTime(Text(time?["modified"])) ?? ordered.LastOrDefault()?.Published ?? created;

        return new NpmPackageInfo(name, created, modified, Tags(document), ordered);
    }

    private static Dictionary<string, string> Tags(JsonObject document)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in (document["dist-tags"] as JsonObject) ?? new JsonObject())
        {
            if (Text(pair.Value) is { } version) tags[pair.Key] = version;
        }

        return tags;
    }

    // The manifest of 'version' in a packument, matching by semantic version (build metadata ignored); the upstream's spelling is returned.
    private static JsonObject? FindVersion(JsonObject document, string version, out string? upstreamVersion)
    {
        upstreamVersion = null;
        if (document["versions"] is not JsonObject versions) return null;

        // The exact spelling first: an upstream may list versions that differ only in case.
        if (versions[version] is JsonObject exact)
        {
            upstreamVersion = version;

            return exact;
        }

        var wanted = NpmNames.Comparable(version);
        foreach (var pair in versions)
        {
            if (pair.Value is JsonObject manifest && (pair.Key == version || NpmNames.Comparable(pair.Key) == wanted))
            {
                upstreamVersion = pair.Key;

                return manifest;
            }
        }

        return null;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static DateTime? ParseTime(string? text)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    private static async Task<(long Length, string Integrity, string Shasum)> CopyAndHashAsync(Stream source, Stream target, CancellationToken cancellationToken)
    {
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha512.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            length += read;
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);

        return (length, "sha512-" + Convert.ToBase64String(sha512.GetHashAndReset()), Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant());
    }

    // Disposing the stream releases the download (its connection) too.
    private sealed class OwningStream : Stream
    {
        private readonly NpmUpstreamDownload _download;

        public OwningStream(NpmUpstreamDownload download)
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
}

/// <summary>Orders version strings by semantic version (an unparsable one by text).</summary>
internal sealed class NpmVersionOrder : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        var okX = NuGet.Versioning.SemanticVersion.TryParse(x ?? string.Empty, out var a);
        var okY = NuGet.Versioning.SemanticVersion.TryParse(y ?? string.Empty, out var b);
        if (okX && okY) return a!.CompareTo(b!);

        return string.CompareOrdinal(x, y);
    }
}
