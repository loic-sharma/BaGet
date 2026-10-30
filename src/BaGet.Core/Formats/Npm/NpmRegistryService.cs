using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>A version a remote repository fetched from its upstream and keeps, with what the upstream said about the package.</summary>
/// <param name="Name">The package name.</param>
/// <param name="Version">The version, as the upstream names it.</param>
/// <param name="Manifest">The version's manifest as the upstream sent it.</param>
/// <param name="Published">The upstream's publish time for the version.</param>
/// <param name="Created">The upstream's creation time of the package.</param>
/// <param name="Modified">The upstream's last change of the package.</param>
/// <param name="Deprecated">The version's deprecation message, or null.</param>
/// <param name="Integrity">The SRI string of the stored bytes (computed here, not the upstream's claim).</param>
/// <param name="Shasum">The SHA-1 of the stored bytes in hex.</param>
/// <param name="Length">The size of the stored bytes.</param>
/// <param name="Tags">The upstream's dist-tags; those that point at a stored version are kept.</param>
public sealed record NpmCachedVersion(
    string Name,
    string Version,
    JsonObject Manifest,
    DateTime Published,
    DateTime Created,
    DateTime Modified,
    string? Deprecated,
    string Integrity,
    string Shasum,
    long Length,
    IReadOnlyDictionary<string, string> Tags);

/// <summary>
/// <see cref="INpmRegistry"/> over the npm tables and the storage service. Rows live in the repository of the request (the
/// context's query filter and stamping), tarballs under <c>npm/{name}/-/{file}</c> of that repository's storage prefix.
/// </summary>
public class NpmRegistryService : INpmRegistry
{
    private const string TarballContentType = "application/octet-stream";

    private readonly IContext _context;
    private readonly IStorageService _storage;
    private readonly IRepositoryContext _repository;
    private readonly IPackageAuthorizationService _authorization;
    private readonly IUrlGenerator _urls;
    private readonly SystemTime _time;
    private readonly MaintenanceGate _gate;
    private readonly IOptionsSnapshot<NpmOptions>? _options;
    private readonly ArtifactUsageRecorder? _usage;
    private readonly IScanGate? _scanGate;
    private readonly ILogger<NpmRegistryService> _logger;

    /// <param name="context">The database.</param>
    /// <param name="storage">Where tarballs are kept.</param>
    /// <param name="repository">The repository of the request.</param>
    /// <param name="authorization">Decides who may publish, tag, deprecate and unpublish.</param>
    /// <param name="urls">Builds the registry address that tarball URLs start with.</param>
    /// <param name="time">The clock.</param>
    /// <param name="gate">The read-only gate, checked before a tarball is stored.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The <c>Npm:*</c> options.</param>
    /// <param name="usage">Notes downloads for the retention rules; null records nothing.</param>
    /// <param name="scanGate">The vulnerability scanning policy checked before a publish and told of what was stored.</param>
    public NpmRegistryService(
        IContext context,
        IStorageService storage,
        IRepositoryContext repository,
        IPackageAuthorizationService authorization,
        IUrlGenerator urls,
        SystemTime time,
        MaintenanceGate gate,
        ILogger<NpmRegistryService> logger,
        IOptionsSnapshot<NpmOptions>? options = null,
        ArtifactUsageRecorder? usage = null,
        IScanGate? scanGate = null)
    {
        _usage = usage;
        _scanGate = scanGate;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options;
    }

    private TimeSpan UnpublishWindow => _options?.Value.UnpublishWindow ?? TimeSpan.FromHours(72);

    // ---- reads ----------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<NpmPackageInfo?> GetPackageAsync(string name, CancellationToken cancellationToken)
    {
        var package = await _context.NpmPackages!.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        if (package == null) return null;

        var versions = await _context.NpmVersions!.AsNoTracking().Where(v => v.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var tags = await _context.NpmDistTags!.AsNoTracking().Where(t => t.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);

        return new NpmPackageInfo(
            package.Name!,
            DateTime.SpecifyKind(package.Created, DateTimeKind.Utc),
            DateTime.SpecifyKind(package.Modified, DateTimeKind.Utc),
            tags.ToDictionary(t => t.Tag!, t => t.Version!, StringComparer.Ordinal),
            versions.Select(ToInfo).OrderBy(v => v.Version, new SemanticVersionComparer()).ToList());
    }

    private static NpmVersionInfo ToInfo(NpmVersion row)
    {
        JsonObject manifest;
        try
        {
            manifest = JsonNode.Parse(row.Manifest ?? "{}") as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            manifest = new JsonObject();
        }

        return new NpmVersionInfo(
            row.Version!,
            DateTime.SpecifyKind(row.Published, DateTimeKind.Utc),
            row.Deprecated,
            row.TarballSize,
            row.Integrity!,
            row.Shasum!,
            manifest);
    }

    private sealed class SemanticVersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            var okX = SemanticVersion.TryParse(x ?? string.Empty, out var a);
            var okY = SemanticVersion.TryParse(y ?? string.Empty, out var b);
            if (okX && okY) return a!.CompareTo(b!);

            return string.CompareOrdinal(x, y);
        }
    }

    /// <inheritdoc/>
    public async Task<JsonObject?> GetPackumentAsync(string name, bool abbreviated, CancellationToken cancellationToken)
    {
        var package = await GetPackageAsync(name, cancellationToken).ConfigureAwait(false);

        return package == null ? null : BuildPackument(package, abbreviated);
    }

    // The fields `npm install` reads (the registry's "corgi" document); everything else is dropped to keep the answer small.
    private static readonly string[] InstallFields =
    {
        "name", "version", "dependencies", "optionalDependencies", "peerDependencies", "peerDependenciesMeta", "bundleDependencies",
        "bundledDependencies", "bin", "directories", "engines", "funding", "os", "cpu", "libc", "dist", "deprecated",
        "hasInstallScript", "_hasShrinkwrap",
    };

    private JsonObject BuildPackument(NpmPackageInfo package, bool abbreviated)
    {
        var registry = _urls.GetNpmRegistryUrl();
        var versions = new JsonObject();
        var time = new JsonObject
        {
            ["created"] = Iso(package.Created),
            ["modified"] = Iso(package.Modified),
        };

        foreach (var version in package.Versions)
        {
            var manifest = abbreviated ? Pick(version.Manifest) : (JsonObject)version.Manifest.DeepClone();

            var dist = manifest["dist"] as JsonObject ?? new JsonObject();
            dist["tarball"] = $"{registry}{package.Name}/-/{NpmNames.TarballFileName(package.Name, version.Version)}";
            dist["integrity"] = version.Integrity;
            dist["shasum"] = version.Shasum;
            manifest["dist"] = dist;
            manifest["name"] = package.Name;
            manifest["version"] = version.Version;
            if (version.Deprecated != null) manifest["deprecated"] = version.Deprecated; else manifest.Remove("deprecated");
            if (!abbreviated) manifest["_id"] = $"{package.Name}@{version.Version}";

            versions[version.Version] = manifest;
            time[version.Version] = Iso(version.Published);
        }

        var tags = new JsonObject();
        foreach (var tag in package.DistTags.OrderBy(t => t.Key, StringComparer.Ordinal)) tags[tag.Key] = tag.Value;

        if (abbreviated)
        {
            return new JsonObject
            {
                ["name"] = package.Name,
                ["modified"] = Iso(package.Modified),
                ["dist-tags"] = tags,
                ["versions"] = versions,
            };
        }

        var latest = package.Latest;
        var packument = new JsonObject
        {
            ["_id"] = package.Name,
            ["_rev"] = package.Revision,
            ["name"] = package.Name,
            ["dist-tags"] = tags,
            ["versions"] = versions,
            ["time"] = time,
        };

        if (latest?.Description != null) packument["description"] = latest.Description;
        if (latest?.Readme != null) packument["readme"] = latest.Readme;
        foreach (var field in new[] { "license", "homepage", "keywords", "repository", "author", "maintainers", "bugs" })
        {
            if (latest?.Manifest[field] is JsonNode node) packument[field] = node.DeepClone();
        }

        return packument;
    }

    private static JsonObject Pick(JsonObject manifest)
    {
        var picked = new JsonObject();
        foreach (var field in InstallFields)
        {
            if (manifest[field] is JsonNode node) picked[field] = node.DeepClone();
        }

        return picked;
    }

    private static string Iso(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public async Task<NpmTarball?> OpenTarballAsync(string name, string file, CancellationToken cancellationToken)
    {
        if (!NpmNames.TryParseTarballFile(name, file, out var version)) return null;

        var row = await _context.NpmVersions!.AsNoTracking()
            .Join(_context.NpmPackages!.Where(p => p.Name == name), v => v.PackageKey, p => p.Key, (v, p) => v)
            .FirstOrDefaultAsync(v => v.Version == version, cancellationToken).ConfigureAwait(false);
        if (row == null) return null;

        try
        {
            var content = await _storage.GetAsync(StoragePath(name, version), cancellationToken).ConfigureAwait(false);

            // For the retention rules: the version was just downloaded.
            _usage?.Touch(_repository.Current.Key, name, version);

            return new NpmTarball(content, row.TarballSize, row.Integrity!);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogError("The tarball of {Package}@{Version} is missing from storage.", name, version);

            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string name, string version, CancellationToken cancellationToken)
    {
        var stored = await _context.NpmVersions!.AsNoTracking()
            .Join(_context.NpmPackages!.Where(p => p.Name == name), v => v.PackageKey, p => p.Key, (v, p) => v.Version!)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return stored.Any(v => NpmNames.Comparable(v) == NpmNames.Comparable(version)) ? ResolutionOutcome.Stored : ResolutionOutcome.NotFound;
    }

    private static readonly KeyedLocks PublishLocks = new();

    // Versions that npm (or a case-insensitive store) treats as one share a lock: build metadata and prerelease case ignored.
    private string LockKey(string name, string version) => StoragePath(name, NpmNames.Comparable(version)).ToLowerInvariant();

    private string StoragePath(string name, string version) => RepositoryStorage.Scope(_repository.Current, NpmNames.TarballPath(name, version));

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>?> GetDistTagsAsync(string name, CancellationToken cancellationToken)
    {
        var package = await _context.NpmPackages!.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        if (package == null) return null;

        var tags = await _context.NpmDistTags!.AsNoTracking().Where(t => t.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);

        return tags.ToDictionary(t => t.Tag!, t => t.Version!, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public async Task<NpmSearchPage> SearchAsync(
        string? text,
        int skip,
        int take,
        Func<string, CancellationToken, Task<bool>>? canRead = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<NpmPackage> query = _context.NpmPackages!.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim().ToLowerInvariant();
            query = query.Where(p => p.Name!.Contains(needle));
        }

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 0, 250);

        int total;
        List<NpmPackage> page;
        if (canRead == null)
        {
            total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            page = await query.OrderBy(p => p.Name).Skip(skip).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Only the packages the caller may read count: the names are ordered, filtered and paged here, and only the
            // page's packages are loaded in full.
            var visible = new List<string>();
            foreach (var name in await query.OrderBy(p => p.Name).Select(p => p.Name!).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await canRead(name, cancellationToken).ConfigureAwait(false)) visible.Add(name);
            }

            total = visible.Count;
            var names = visible.Skip(skip).Take(take).ToList();
            page = (await _context.NpmPackages!.AsNoTracking().Where(p => names.Contains(p.Name!)).ToListAsync(cancellationToken).ConfigureAwait(false))
                .OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        }

        var keys = page.Select(p => p.Key).ToList();
        var tags = await _context.NpmDistTags!.AsNoTracking().Where(t => keys.Contains(t.PackageKey) && t.Tag == "latest").ToListAsync(cancellationToken).ConfigureAwait(false);
        var wanted = page.Select(p => (p.Key, Version: tags.FirstOrDefault(t => t.PackageKey == p.Key)?.Version)).ToList();
        var versions = await _context.NpmVersions!.AsNoTracking().Where(v => keys.Contains(v.PackageKey)).ToListAsync(cancellationToken).ConfigureAwait(false);

        var hits = new List<NpmSearchHit>();
        foreach (var package in page)
        {
            var own = versions.Where(v => v.PackageKey == package.Key).ToList();
            var taggedVersion = wanted.First(w => w.Key == package.Key).Version;
            var chosen = own.FirstOrDefault(v => v.Version == taggedVersion)
                         ?? own.FirstOrDefault(v => v.Version == NpmNames.Highest(own.Select(o => o.Version!)));
            var info = chosen == null ? null : ToInfo(chosen);

            hits.Add(new NpmSearchHit(package.Name!, info?.Version, info?.Description, info?.Keywords ?? Array.Empty<string>(), DateTime.SpecifyKind(package.Modified, DateTimeKind.Utc)));
        }

        return new NpmSearchPage(total, hits);
    }

    // ---- authorization --------------------------------------------------------------------------------------------------

    private async Task<NpmResult?> DenyAsync(BaGetPrincipal? caller, PackageAction action, string name, CancellationToken cancellationToken)
    {
        if (caller == null) return NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.");

        if (_repository.Current.Kind != RepositoryKind.Local)
        {
            return NpmResult.Fail(NpmStatus.Forbidden, "This repository takes no changes.");
        }

        if (!await _authorization.AuthorizeAsync(caller, action, name, cancellationToken).ConfigureAwait(false))
        {
            return NpmResult.Fail(NpmStatus.Forbidden, $"You are not allowed to {Describe(action)} {name}.");
        }

        return null;
    }

    /// <summary>Null when the caller may do at least one of <paramref name="actions"/>, else the last refusal.</summary>
    private async Task<NpmResult?> DenyAnyAsync(BaGetPrincipal? caller, string name, CancellationToken cancellationToken, params PackageAction[] actions)
    {
        NpmResult? refusal = null;
        foreach (var action in actions)
        {
            refusal = await DenyAsync(caller, action, name, cancellationToken).ConfigureAwait(false);
            if (refusal == null) return null;
        }

        return refusal;
    }

    private static string Describe(PackageAction action) => action switch
    {
        PackageAction.Push => "publish to",
        PackageAction.Annotate => "deprecate",
        _ => "delete from",
    };

    private static bool TryName(string name, out string normalized, out NpmResult? failure)
    {
        failure = null;
        if (NpmNames.TryParse(name, out normalized, out var error)) return true;

        failure = NpmResult.Fail(NpmStatus.Invalid, error);
        return false;
    }

    // ---- remote cache ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Keeps a version a remote repository fetched: the tarball file, the package and version rows and the tags that point at
    /// stored versions, in one transaction. No authorization (the request only read), but the maintenance gate applies and
    /// nothing is stored if a concurrent request stored the version first (its file stays). Called by
    /// <see cref="RemoteNpmRegistry"/>.
    /// </summary>
    /// <param name="cached">What to keep.</param>
    /// <param name="tarball">The bytes, seekable; read from the start, the caller disposes it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>True when this call stored the version, false when it was already stored.</returns>
    /// <exception cref="MaintenanceException">The gate is closed.</exception>
    internal async Task<bool> StoreCachedAsync(NpmCachedVersion cached, Stream tarball, CancellationToken cancellationToken)
    {
        _gate.ThrowIfClosed();

        var name = cached.Name;
        var version = cached.Version;
        var path = StoragePath(name, version);

        using var publishLock = await PublishLocks.LockAsync(LockKey(name, version), cancellationToken).ConfigureAwait(false);
        var package = await _context.NpmPackages!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        var storedRows = package == null
            ? new List<NpmVersion>()
            : await _context.NpmVersions!.Where(v => v.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (storedRows.Any(v => string.Equals(v.Version, version, StringComparison.Ordinal))) return false;

        // A row that differs only in case or build metadata is a copy cached under another spelling (by an earlier release);
        // the upstream's spelling replaces it so the file name that is asked for can be served.
        var lookalikes = storedRows.Where(v => NpmNames.Comparable(v.Version!) == NpmNames.Comparable(version)).ToList();
        var stored = storedRows.Except(lookalikes).Select(v => v.Version!).ToList();

        tarball.Position = 0;
        var put = await _storage.PutAsync(path, tarball, TarballContentType, cancellationToken).ConfigureAwait(false);
        if (put == StoragePutResult.Conflict)
        {
            // A file of an earlier attempt that never got its row.
            await _storage.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
            tarball.Position = 0;
            put = await _storage.PutAsync(path, tarball, TarballContentType, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            if (lookalikes.Count > 0)
            {
                var lookalikeNames = lookalikes.Select(l => l.Version!).ToHashSet(StringComparer.Ordinal);
                _context.NpmDistTags!.RemoveRange(await _context.NpmDistTags!.Where(t => t.PackageKey == package!.Key && lookalikeNames.Contains(t.Version!)).ToListAsync(cancellationToken).ConfigureAwait(false));
                _context.NpmVersions!.RemoveRange(lookalikes);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            if (package == null)
            {
                package = new NpmPackage { Name = name, Created = cached.Created, Modified = cached.Modified };
                _context.NpmPackages!.Add(package);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _context.NpmVersions!.Add(new NpmVersion
            {
                PackageKey = package.Key,
                Version = version,
                Integrity = cached.Integrity,
                Shasum = cached.Shasum,
                TarballSize = cached.Length,
                Manifest = BuildStoredManifest(cached.Manifest, name, version, cached.Integrity, cached.Shasum).ToJsonString(),
                Deprecated = string.IsNullOrWhiteSpace(cached.Deprecated) ? null : cached.Deprecated,
                Published = cached.Published,
            });

            // The upstream's tags, for the versions this repository holds; "latest" always exists (the highest held version
            // when the upstream's own is not held, which moves up as higher ones are kept), so the stored copy can answer a packument by itself.
            stored.Add(version);
            var tags = cached.Tags
                .Where(t => NpmNames.IsValidTag(t.Key, out _) && stored.Contains(t.Value, StringComparer.Ordinal))
                .ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
            foreach (var tag in tags) await SetTagAsync(package.Key, tag.Key, tag.Value, cancellationToken).ConfigureAwait(false);

            if (!tags.ContainsKey("latest"))
            {
                var highest = NpmNames.Highest(stored);
                if (highest != null) await SetTagAsync(package.Key, "latest", highest, cancellationToken).ConfigureAwait(false);
            }

            if (cached.Modified > package.Modified) package.Modified = cached.Modified;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            foreach (var lookalike in lookalikes)
            {
                var old = StoragePath(name, lookalike.Version!);
                if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) await TryDeleteAsync(old).ConfigureAwait(false);
            }
        }
        catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e))
        {
            // A concurrent request stored the same version; its file is the one at the path.
            return false;
        }
        catch
        {
            if (put == StoragePutResult.Success) await TryDeleteAsync(path).ConfigureAwait(false);

            throw;
        }

        if (_scanGate != null) await _scanGate.RegisterStoredAsync(name, version, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Deletes what a remote repository stored (rows and files), without authorization: the administrator's purge. One package
    /// (every version, or the one given, matched by semantic version) or, with no name, every package. Tags that pointed at a
    /// deleted version are removed, and <c>latest</c> moves to the highest version left. Called by
    /// <see cref="RemoteRepositoryAdministration"/>.
    /// </summary>
    /// <returns>The versions deleted.</returns>
    internal async Task<int> PurgeAsync(string? name, string? version, CancellationToken cancellationToken)
    {
        _gate.ThrowIfClosed();
        if (name == null && version != null) throw new ArgumentException("A version needs the package it belongs to.", nameof(version));

        var deleted = 0;
        while (true)
        {
            var query = _context.NpmPackages!.AsQueryable();
            if (name != null) query = query.Where(p => p.Name == name);

            // A purge of everything empties 100 packages per round (each round removes the packages it handled); one package is a single round.
            var packages = await query.OrderBy(p => p.Key).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (packages.Count == 0) return deleted;

            foreach (var package in packages)
            {
                var rows = await _context.NpmVersions!.Where(v => v.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
                var removed = version == null
                    ? rows
                    : rows.Where(r => NpmNames.Comparable(r.Version!) == NpmNames.Comparable(version)).ToList();
                if (removed.Count == 0) continue;

                var tags = await _context.NpmDistTags!.Where(t => t.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
                _context.NpmVersions!.RemoveRange(removed);
                var kept = rows.Except(removed).Select(r => r.Version!).ToHashSet(StringComparer.Ordinal);
                if (kept.Count == 0)
                {
                    _context.NpmDistTags!.RemoveRange(tags);
                    _context.NpmPackages!.Remove(package);
                }
                else
                {
                    foreach (var tag in tags.Where(t => t.Tag != "latest" && !kept.Contains(t.Version!))) _context.NpmDistTags!.Remove(tag);

                    // "latest" always points at a version that is still held.
                    var latest = tags.FirstOrDefault(t => t.Tag == "latest");
                    if (latest != null && !kept.Contains(latest.Version!)) latest.Version = NpmNames.Highest(kept);
                    package.Modified = _time.UtcNow;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                foreach (var row in removed) await TryDeleteAsync(StoragePath(package.Name!, row.Version!)).ConfigureAwait(false);

                deleted += removed.Count;
            }

            if (name != null) return deleted;
        }
    }

    // ---- publish --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// What <see cref="PublishAsync"/> would decide from the tarball alone (the same scanning check), without storing anything: a
    /// replication that replaces a stored version asks first, so a refusal cannot cost the copy it was to replace.
    /// </summary>
    /// <exception cref="ScanBlockedException">The scanning policy refuses the package.</exception>
    internal async Task PreflightReplacementAsync(string name, string version, JsonObject manifest, Stream tarball, string? integrity, CancellationToken cancellationToken)
    {
        tarball.Position = 0;
        var (_, actual, _) = await HashAsync(tarball, cancellationToken).ConfigureAwait(false);
        if (integrity != null && !string.Equals(integrity, actual, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The tarball does not match the integrity (sha512) the peer announced.");
        }

        tarball.Position = 0;
        var inner = await NpmTarballReader.ReadPackageJsonAsync(tarball, cancellationToken).ConfigureAwait(false);
        tarball.Position = 0;
        if (inner == null || _scanGate == null || !_scanGate.Enabled || !await _scanGate.BlocksDeploysAsync(cancellationToken).ConfigureAwait(false)) return;

        var components = NpmScanComponents.Of(name, version, inner).Union(NpmScanComponents.Of(name, version, manifest)).ToList();
        await _scanGate.CheckDeployAsync(components, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<NpmResult> PublishAsync(BaGetPrincipal? caller, NpmPublication publication, CancellationToken cancellationToken)
    {
        if (publication == null) throw new ArgumentNullException(nameof(publication));

        if (!TryName(publication.Name, out var name, out var failure)) return failure!;

        if (!NpmNames.TryParseVersion(publication.Version, out var version))
        {
            return NpmResult.Fail(NpmStatus.Invalid, $"'{publication.Version}' is not a valid semantic version.");
        }

        if (!NpmNames.IsValidTag(publication.Tag, out var tagError)) return NpmResult.Fail(NpmStatus.Invalid, tagError);

        var denied = await DenyAsync(caller, PackageAction.Push, name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        // Hash and check the bytes before anything is stored: what the client computed must be what arrived.
        var tarball = publication.Tarball;
        tarball.Position = 0;
        var (length, integrity, shasum) = await HashAsync(tarball, cancellationToken).ConfigureAwait(false);

        if (publication.ExpectedLength is long declared && declared != length)
        {
            return NpmResult.Fail(NpmStatus.Invalid, $"The tarball has {length} bytes, not the {declared} the request declares.");
        }

        if (publication.ExpectedIntegrity != null && !string.Equals(publication.ExpectedIntegrity, integrity, StringComparison.Ordinal))
        {
            return NpmResult.Fail(NpmStatus.Invalid, "The tarball does not match the integrity (sha512) the request declares.");
        }

        if (publication.ExpectedShasum != null && !string.Equals(publication.ExpectedShasum, shasum, StringComparison.OrdinalIgnoreCase))
        {
            return NpmResult.Fail(NpmStatus.Invalid, "The tarball does not match the shasum (sha1) the request declares.");
        }

        // The tarball is the package it says it is.
        tarball.Position = 0;
        var inner = await NpmTarballReader.ReadPackageJsonAsync(tarball, cancellationToken).ConfigureAwait(false);
        if (inner == null) return NpmResult.Fail(NpmStatus.Invalid, "The tarball is not a gzip-compressed tar with a package.json in its top folder.");

        var innerName = inner["name"] is JsonValue n && n.TryGetValue<string>(out var innerNameText) ? innerNameText : null;
        var innerVersion = inner["version"] is JsonValue v && v.TryGetValue<string>(out var innerVersionText) ? innerVersionText : null;
        if (!string.Equals(innerName, name, StringComparison.Ordinal) || !string.Equals(innerVersion, version, StringComparison.Ordinal))
        {
            return NpmResult.Fail(NpmStatus.Invalid, $"The tarball's package.json is {innerName}@{innerVersion}, not {name}@{version}.");
        }

        // The scanning policy of the repository may refuse a package with known vulnerabilities (ScanBlockedException, 403).
        if (_scanGate != null && _scanGate.Enabled && await _scanGate.BlocksDeploysAsync(cancellationToken).ConfigureAwait(false))
        {
            // What the tarball declares and what the packument will tell clients (the request's version object) are both checked.
            var components = NpmScanComponents.Of(name, version, inner).Union(NpmScanComponents.Of(name, version, publication.Manifest)).ToList();
            await _scanGate.CheckDeployAsync(components, cancellationToken).ConfigureAwait(false);
        }

        _gate.ThrowIfClosed();

        // One publish of a version (build metadata and prerelease case ignored) at a time in this process: the clash check and
        // the file write must not interleave with a concurrent publish, or the loser would replace the winner's file. Under the
        // lock the rows decide, so a loser is refused without touching storage. Other nodes sharing the store are not fenced.
        using var publishLock = await PublishLocks.LockAsync(LockKey(name, version), cancellationToken).ConfigureAwait(false);

        var package = await _context.NpmPackages!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        var isNewPackage = package == null;
        if (package != null)
        {
            var existing = await _context.NpmVersions!.AsNoTracking().Where(x => x.PackageKey == package.Key).Select(x => x.Version!).ToListAsync(cancellationToken).ConfigureAwait(false);
            var clash = existing.FirstOrDefault(x => NpmNames.Comparable(x) == NpmNames.Comparable(version));
            if (clash != null)
            {
                return NpmResult.Fail(NpmStatus.Conflict, $"You cannot publish over the previously published version {clash}.");
            }
        }

        var path = StoragePath(name, version);
        tarball.Position = 0;
        var stored = await _storage.PutAsync(path, tarball, TarballContentType, cancellationToken).ConfigureAwait(false);
        if (stored == StoragePutResult.Conflict)
        {
            // A file of an earlier attempt that never got its row: the version is free, so the file is replaced.
            await _storage.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
            tarball.Position = 0;
            stored = await _storage.PutAsync(path, tarball, TarballContentType, cancellationToken).ConfigureAwait(false);
        }

        var now = _time.UtcNow;
        var storedByThisCall = stored == StoragePutResult.Success;

        // Two first publishes of different versions of one new package both find no package row; the loser's insert of the
        // package hits the unique (repository, name) index. It then publishes onto the winner's row: only a clash on the
        // version itself is a conflict.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                if (package == null)
                {
                    package = new NpmPackage { Name = name, Created = now, Modified = now };
                    _context.NpmPackages!.Add(package);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                _context.NpmVersions!.Add(new NpmVersion
                {
                    PackageKey = package.Key,
                    Version = version,
                    Integrity = integrity,
                    Shasum = shasum,
                    TarballSize = length,
                    Manifest = BuildStoredManifest(publication.Manifest, name, version, integrity, shasum, inner).ToJsonString(),
                    Published = now,
                });

                await SetTagAsync(package.Key, publication.Tag, version, cancellationToken).ConfigureAwait(false);

                // The first version of a package is always "latest", whatever tag it was published with: a package without it
                // cannot be installed by name.
                if (isNewPackage && publication.Tag != "latest") await SetTagAsync(package.Key, "latest", version, cancellationToken).ConfigureAwait(false);

                package.Modified = now;
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e))
            {
                // The rolled-back attempt left tracked entities that no longer exist in the database.
                (_context as DbContext)?.ChangeTracker.Clear();

                var winner = isNewPackage && attempt == 0
                    ? await _context.NpmPackages!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false)
                    : null;
                if (winner != null)
                {
                    package = winner;
                    isNewPackage = false;
                    continue;
                }

                // A clash on the version: a concurrent publish of the same version won. Its file is the one at the path while
                // its row exists; when no row has this version the file is this call's own leftover, and goes.
                var taken = package != null
                    && await _context.NpmVersions!.AsNoTracking().AnyAsync(v => v.PackageKey == package.Key && v.Version == version, cancellationToken).ConfigureAwait(false);
                if (storedByThisCall && !taken) await TryDeleteAsync(path).ConfigureAwait(false);

                return NpmResult.Fail(NpmStatus.Conflict, $"You cannot publish over the previously published version {version}.");
            }
            catch
            {
                if (storedByThisCall) await TryDeleteAsync(path).ConfigureAwait(false);

                throw;
            }
        }

        await _authorization.OnPushedAsync(caller!, name, isNewPackage, cancellationToken).ConfigureAwait(false);
        if (_scanGate != null) await _scanGate.RegisterStoredAsync(name, version, cancellationToken).ConfigureAwait(false);

        return NpmResult.Created($"Published {name}@{version}.", name);
    }

    /// <inheritdoc/>
    public async Task<NpmResult> PublishTarballAsync(BaGetPrincipal? caller, Stream tarball, string tag, CancellationToken cancellationToken)
    {
        if (tarball == null) throw new ArgumentNullException(nameof(tarball));

        tarball.Position = 0;
        var inner = await NpmTarballReader.ReadPackageJsonAsync(tarball, cancellationToken).ConfigureAwait(false);
        if (inner == null) return NpmResult.Fail(NpmStatus.Invalid, "The file is not an npm tarball: a gzip-compressed tar with a package.json in its top folder.");

        var name = inner["name"] is JsonValue n && n.TryGetValue<string>(out var nameText) ? nameText : null;
        var version = inner["version"] is JsonValue v && v.TryGetValue<string>(out var versionText) ? versionText : null;
        if (name == null || version == null) return NpmResult.Fail(NpmStatus.Invalid, "The package.json has no name or no version.");

        // Authorize before reading the file again: an unauthorized caller learns nothing about the repository.
        if (!TryName(name, out var normalized, out var failure)) return failure!;

        return await PublishAsync(caller, new NpmPublication(normalized, version, inner, string.IsNullOrWhiteSpace(tag) ? "latest" : tag.Trim(), tarball), cancellationToken).ConfigureAwait(false);
    }

    // The fields npm acts on at install time come from the tarball's own package.json when there is one (a publisher cannot
    // declare harmless dependencies or no install script in the request and ship others); the registry owns name, version and dist.
    private static readonly string[] TarballOwnedFields =
    {
        "dependencies", "optionalDependencies", "peerDependencies", "peerDependenciesMeta", "bundleDependencies", "bundledDependencies",
        "bin", "directories", "engines", "funding", "os", "cpu", "libc", "scripts",
    };

    private static JsonObject BuildStoredManifest(JsonObject sent, string name, string version, string integrity, string shasum, JsonObject? tarballPackageJson = null)
    {
        var manifest = (JsonObject)sent.DeepClone();

        if (tarballPackageJson != null)
        {
            foreach (var field in TarballOwnedFields)
            {
                if (tarballPackageJson[field] is { } value) manifest[field] = value.DeepClone(); else manifest.Remove(field);
            }

            // npm normalizes at publish time what the tarball's own package.json lacks: a binding.gyp becomes
            // scripts.install = "node-gyp rebuild" (+ gypfile), directories.bin becomes bin. The tarball is not scanned for
            // binding.gyp; the client's normalized value is kept only in these two exact shapes, which can over-report an
            // install script but never hide one.
            var tarballScripts = tarballPackageJson["scripts"] as JsonObject;
            if (!(tarballScripts?.ContainsKey("install") ?? false) && !(tarballScripts?.ContainsKey("preinstall") ?? false)
                && sent["scripts"] is JsonObject sentScripts
                && sentScripts["install"] is JsonValue gypInstall && gypInstall.TryGetValue<string>(out var gypText) && gypText == "node-gyp rebuild")
            {
                var merged = tarballScripts != null ? (JsonObject)tarballScripts.DeepClone() : new JsonObject();
                merged["install"] = gypText;
                manifest["scripts"] = merged;
                manifest["gypfile"] = true;
            }
            else
            {
                manifest.Remove("gypfile");
            }

            if (tarballPackageJson["bin"] == null && tarballPackageJson["directories"] is JsonObject tarballDirectories
                && tarballDirectories["bin"] != null && sent["bin"] is { } sentBin)
            {
                manifest["bin"] = sentBin.DeepClone();
            }

            var finalScripts = manifest["scripts"] as JsonObject;
            var hasInstallScript = finalScripts != null
                && (finalScripts.ContainsKey("preinstall") || finalScripts.ContainsKey("install") || finalScripts.ContainsKey("postinstall"));
            if (hasInstallScript) manifest["hasInstallScript"] = true; else manifest.Remove("hasInstallScript");
        }

        // The registry decides these; the client's copies are discarded.
        manifest.Remove("_attachments");
        manifest.Remove("deprecated");
        manifest.Remove("dist-tags");
        manifest["name"] = name;
        manifest["version"] = version;

        var dist = new JsonObject();
        if (sent["dist"] is JsonObject sentDist)
        {
            foreach (var property in sentDist)
            {
                if (property.Key is "tarball" or "integrity" or "shasum") continue;

                dist[property.Key] = property.Value?.DeepClone();
            }
        }

        dist["integrity"] = integrity;
        dist["shasum"] = shasum;
        manifest["dist"] = dist;

        return manifest;
    }

    private static async Task<(long Length, string Integrity, string Shasum)> HashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha512.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
            length += read;
        }

        return (length, "sha512-" + Convert.ToBase64String(sha512.GetHashAndReset()), Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant());
    }

    private async Task TryDeleteAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync(path, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not delete {Path}.", path);
        }
    }

    // Points a tag at a version, keeping one row per (package, tag) as the unique index demands.
    private async Task SetTagAsync(int packageKey, string tag, string version, CancellationToken cancellationToken)
    {
        var existing = await _context.NpmDistTags!.FirstOrDefaultAsync(t => t.PackageKey == packageKey && t.Tag == tag, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            existing.Version = version;
        }
        else
        {
            _context.NpmDistTags!.Add(new NpmDistTag { PackageKey = packageKey, Tag = tag, Version = version });
        }
    }

    // ---- dist-tags ------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<NpmResult> SetDistTagAsync(BaGetPrincipal? caller, string name, string tag, string version, CancellationToken cancellationToken)
    {
        if (!TryName(name, out name, out var failure)) return failure!;
        if (!NpmNames.IsValidTag(tag, out var tagError)) return NpmResult.Fail(NpmStatus.Invalid, tagError);

        if (caller == null) return NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.");

        var denied = await DenyAsync(caller, PackageAction.Push, name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        var package = await _context.NpmPackages!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        if (package == null) return NpmResult.Fail(NpmStatus.NotFound, $"There is no package {name}.");

        var rows = await _context.NpmVersions!.AsNoTracking().Where(v => v.PackageKey == package.Key).Select(v => v.Version!).ToListAsync(cancellationToken).ConfigureAwait(false);
        var stored = rows.FirstOrDefault(v => v == version) ?? rows.FirstOrDefault(v => NpmNames.Comparable(v) == NpmNames.Comparable(version ?? string.Empty));
        if (stored == null) return NpmResult.Fail(NpmStatus.NotFound, $"{name} has no version {version}.");

        await SetTagAsync(package.Key, tag, stored, cancellationToken).ConfigureAwait(false);
        package.Modified = _time.UtcNow;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NpmResult.Ok($"+{tag}: {name}@{stored}");
    }

    /// <inheritdoc/>
    public async Task<NpmResult> RemoveDistTagAsync(BaGetPrincipal? caller, string name, string tag, CancellationToken cancellationToken)
    {
        if (!TryName(name, out name, out var failure)) return failure!;
        if (caller == null) return NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.");

        var denied = await DenyAsync(caller, PackageAction.Push, name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        var package = await _context.NpmPackages!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        if (package == null) return NpmResult.Fail(NpmStatus.NotFound, $"There is no package {name}.");

        var row = await _context.NpmDistTags!.FirstOrDefaultAsync(t => t.PackageKey == package.Key && t.Tag == tag, cancellationToken).ConfigureAwait(false);
        if (row == null) return NpmResult.Fail(NpmStatus.NotFound, $"{name} has no tag {tag}.");

        _context.NpmDistTags!.Remove(row);
        package.Modified = _time.UtcNow;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NpmResult.Ok($"-{tag}: {name}");
    }

    // ---- deprecate ------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<NpmResult> DeprecateAsync(BaGetPrincipal? caller, string name, IReadOnlyDictionary<string, string?> messages, CancellationToken cancellationToken)
    {
        if (!TryName(name, out name, out var failure)) return failure!;
        if (caller == null) return NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.");

        var denied = await DenyAsync(caller, PackageAction.Annotate, name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        var package = await _context.NpmPackages!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        if (package == null) return NpmResult.Fail(NpmStatus.NotFound, $"There is no package {name}.");

        var rows = await _context.NpmVersions!.Where(v => v.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var changed = 0;
        foreach (var message in messages)
        {
            var row = rows.FirstOrDefault(r => r.Version == message.Key);
            if (row == null) return NpmResult.Fail(NpmStatus.NotFound, $"{name} has no version {message.Key}.");

            var text = string.IsNullOrWhiteSpace(message.Value) ? null : message.Value;
            if (row.Deprecated == text) continue;

            row.Deprecated = text;
            changed++;
        }

        if (changed > 0)
        {
            package.Modified = _time.UtcNow;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return NpmResult.Ok();
    }

    // ---- unpublish ------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<NpmResult> UnpublishVersionsAsync(
        BaGetPrincipal? caller,
        string name,
        IReadOnlyCollection<string> versions,
        IReadOnlyDictionary<string, string>? newTags,
        CancellationToken cancellationToken)
    {
        if (!TryName(name, out name, out var failure)) return failure!;
        if (caller == null) return NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.");

        // Authorize before looking the package up, so a caller who may not change it learns nothing about it.
        var preliminary = await DenyAnyAsync(caller, name, cancellationToken, PackageAction.Push, PackageAction.Manage).ConfigureAwait(false);
        if (preliminary != null) return preliminary;

        var package = await _context.NpmPackages!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        if (package == null) return NpmResult.Fail(NpmStatus.NotFound, $"There is no package {name}.");

        // Nothing to remove is an authorization probe: allowed, and nothing happens.
        if (versions.Count == 0) return NpmResult.Ok();

        var rows = await _context.NpmVersions!.Where(v => v.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        var removed = new List<NpmVersion>();
        foreach (var version in versions.Distinct(StringComparer.Ordinal))
        {
            var row = rows.FirstOrDefault(r => r.Version == version);
            if (row == null) return NpmResult.Fail(NpmStatus.NotFound, $"{name} has no version {version}.");

            removed.Add(row);
        }

        return await RemoveAsync(caller, package, rows, removed, newTags, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<NpmResult> UnpublishPackageAsync(BaGetPrincipal? caller, string name, CancellationToken cancellationToken)
    {
        if (!TryName(name, out name, out var failure)) return failure!;
        if (caller == null) return NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.");

        var preliminary = await DenyAnyAsync(caller, name, cancellationToken, PackageAction.Push, PackageAction.Manage).ConfigureAwait(false);
        if (preliminary != null) return preliminary;

        var package = await _context.NpmPackages!.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        if (package == null) return NpmResult.Fail(NpmStatus.NotFound, $"There is no package {name}.");

        var rows = await _context.NpmVersions!.Where(v => v.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);

        return await RemoveAsync(caller, package, rows, rows.ToList(), newTags: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NpmResult> RemoveAsync(
        BaGetPrincipal caller,
        NpmPackage package,
        List<NpmVersion> all,
        List<NpmVersion> removed,
        IReadOnlyDictionary<string, string>? newTags,
        CancellationToken cancellationToken)
    {
        var name = package.Name!;

        // Deploy lets an owner take back a recent mistake (npmjs allows it for 72 hours); anything older is a deletion.
        // Only a key that may push whole packages (the push scope) takes that shortcut: a key limited to pushing new
        // versions (package:pushversion) never deletes, so it is judged as for a deletion (the unlist scope).
        var window = UnpublishWindow;
        var now = _time.UtcNow;
        var young = window > TimeSpan.Zero
                    && caller.HasScope(ApiKeyScopes.Push)
                    && removed.All(r => now - DateTime.SpecifyKind(r.Published, DateTimeKind.Utc) <= window);
        var denied = await DenyAsync(caller, young ? PackageAction.Push : PackageAction.Manage, name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        _gate.ThrowIfClosed();

        // The publish lock of every removed version (in a stable order) is held until the files are gone, so a republish of
        // the same version cannot have its new file deleted by this unpublish.
        var locks = new List<IDisposable>();
        try
        {
            foreach (var key in removed.Select(r => LockKey(name, r.Version!)).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
            {
                locks.Add(await PublishLocks.LockAsync(key, cancellationToken).ConfigureAwait(false));
            }

            return await RemoveLockedAsync(package, all, removed, newTags, now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var held in locks) held.Dispose();
        }
    }

    private async Task<NpmResult> RemoveLockedAsync(
        NpmPackage package,
        List<NpmVersion> all,
        List<NpmVersion> removed,
        IReadOnlyDictionary<string, string>? newTags,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var name = package.Name!;
        var remaining = all.Where(r => !removed.Contains(r)).ToList();
        var tags = await _context.NpmDistTags!.Where(t => t.PackageKey == package.Key).ToListAsync(cancellationToken).ConfigureAwait(false);

        _context.NpmVersions!.RemoveRange(removed);

        if (remaining.Count == 0)
        {
            _context.NpmDistTags!.RemoveRange(tags);
            _context.NpmPackages!.Remove(package);
        }
        else
        {
            var kept = remaining.Select(r => r.Version!).ToHashSet(StringComparer.Ordinal);
            foreach (var tag in tags.ToList())
            {
                if (kept.Contains(tag.Version!)) continue;

                if (newTags != null && newTags.TryGetValue(tag.Tag!, out var target) && kept.Contains(target))
                {
                    tag.Version = target;
                }
                else
                {
                    _context.NpmDistTags!.Remove(tag);
                    tags.Remove(tag);
                }
            }

            // "latest" must point somewhere, or the package cannot be installed by name.
            if (!tags.Any(t => t.Tag == "latest"))
            {
                var highest = NpmNames.Highest(kept);
                if (highest != null) _context.NpmDistTags!.Add(new NpmDistTag { PackageKey = package.Key, Tag = "latest", Version = highest });
            }

            package.Modified = now;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The rows are gone; a failing file delete leaves an unreferenced tarball, not a broken package.
        foreach (var row in removed) await TryDeleteAsync(StoragePath(name, row.Version!)).ConfigureAwait(false);

        return NpmResult.Ok(remaining.Count == 0 ? $"Unpublished {name}." : $"Unpublished {string.Join(", ", removed.Select(r => name + "@" + r.Version))}.");
    }

    /// <inheritdoc/>
    public async Task<NpmResult> DeleteTarballAsync(BaGetPrincipal? caller, string name, string file, CancellationToken cancellationToken)
    {
        if (!TryName(name, out name, out var failure)) return failure!;
        if (!NpmNames.TryParseTarballFile(name, file, out var version)) return NpmResult.Fail(NpmStatus.NotFound, "There is no such file.");
        if (caller == null) return NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.");

        var preliminary = await DenyAnyAsync(caller, name, cancellationToken, PackageAction.Push, PackageAction.Manage).ConfigureAwait(false);
        if (preliminary != null) return preliminary;

        var package = await _context.NpmPackages!.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
        var published = package != null && await _context.NpmVersions!.AnyAsync(v => v.PackageKey == package.Key && v.Version == version, cancellationToken).ConfigureAwait(false);
        if (published) return await UnpublishVersionsAsync(caller, name, new[] { version }, null, cancellationToken).ConfigureAwait(false);

        // A row whose version differs only in case or build metadata owns the file on a case-insensitive store: untouched.
        var lookalike = package != null && (await _context.NpmVersions!.AsNoTracking().Where(v => v.PackageKey == package.Key).Select(v => v.Version!).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Any(v => NpmNames.Comparable(v) == NpmNames.Comparable(version));
        if (lookalike) return NpmResult.Fail(NpmStatus.NotFound, "There is no such file.");

        // The version is already unpublished (the earlier step of the client's sequence): only the file is left.
        var denied = await DenyAsync(caller, PackageAction.Push, name, cancellationToken).ConfigureAwait(false);
        if (denied != null) return denied;

        _gate.ThrowIfClosed();

        // Under the version's lock, and only if no row claims the version now: a publish that finished meanwhile owns the file.
        using var publishLock = await PublishLocks.LockAsync(LockKey(name, version), cancellationToken).ConfigureAwait(false);
        var claimed = package != null && (await _context.NpmVersions!.AsNoTracking().Where(v => v.PackageKey == package.Key).Select(v => v.Version!).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Any(v => NpmNames.Comparable(v) == NpmNames.Comparable(version));
        if (claimed) return NpmResult.Fail(NpmStatus.NotFound, "There is no such file.");

        await _storage.DeleteAsync(StoragePath(name, version), cancellationToken).ConfigureAwait(false);

        return NpmResult.Ok();
    }
}
