using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using BaGet.Catalog.Database;

namespace BaGet.Catalog.Storage;

/// <summary>
/// Appends a catalog entry for every package change. Each entry's leaf document is
/// written to storage first, so a leaf can always be served once its entry exists.
/// </summary>
public class CatalogWriter : IPackageChangeHandler
{
    internal static readonly JsonSerializerOptions LeafSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Commit timestamps must strictly increase, or a mirror reading the catalog
    // "after" its cursor could miss an entry. Commits in this process are serialized.
    // Instances of a cluster are ordered by the per-repository lease "catalog:{repository key}" (IJobLease; without
    // a cluster it is in process and the lock above already serialises).
    private static readonly SemaphoreSlim CommitLock = new(1, 1);

    private readonly ICatalogDatabase _catalog;
    private readonly IPackageDatabase _packages;
    private readonly IPackageStorageService _packageStorage;
    private readonly IStorageService _storage;
    private readonly SystemTime _time;
    private readonly ILogger<CatalogWriter> _logger;
    private readonly MaintenanceGate? _gate;
    private readonly IRepositoryContext? _repository;
    private readonly IJobLease? _lease;

    /// <summary>How long a commit waits for another node's commit of the same repository before it gives up.</summary>
    internal static TimeSpan LeaseWait { get; set; } = TimeSpan.FromSeconds(30);

    public CatalogWriter(
        ICatalogDatabase catalog,
        IPackageDatabase packages,
        IPackageStorageService packageStorage,
        IStorageService storage,
        SystemTime time,
        ILogger<CatalogWriter> logger,
        MaintenanceGate? gate = null,
        IRepositoryContext? repository = null,
        IJobLease? lease = null)
    {
        _lease = lease;
        _gate = gate;
        _repository = repository;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _packageStorage = packageStorage ?? throw new ArgumentNullException(nameof(packageStorage));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Where a leaf document is stored: <c>catalog/{commit}.json</c> for the default repository,
    /// <c>repos/{name}/catalog/{commit}.json</c> for any other (see <see cref="RepositoryStorage"/>).
    /// </summary>
    internal static string LeafPath(RepositoryInfo repository, string? commitId)
        => RepositoryStorage.Scope(repository, $"catalog/{commitId}.json");

    public async Task OnPackageChangedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!_catalog.IsSupported) return;

        var package = await _packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
        if (package == null)
        {
            _logger.LogWarning(
                "Package {PackageId} {PackageVersion} changed but no longer exists; no catalog entry written",
                id,
                version);
            return;
        }

        var (hash, size) = await HashPackageAsync(package, cancellationToken);

        await CommitAsync(
            CatalogEntry.PackageDetailsType,
            package.Id,
            package.Version,
            (commitId, timestamp) => BuildDetailsLeaf(package, hash, size, commitId, timestamp),
            cancellationToken);
    }

    public async Task OnPackageDeletedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!_catalog.IsSupported) return;

        await CommitAsync(
            CatalogEntry.PackageDeleteType,
            id,
            version,
            (commitId, timestamp) => new PackageDeleteCatalogLeaf
            {
                Type = new[] { CatalogEntry.PackageDeleteType, "catalog:Permalink" },
                CommitId = commitId,
                CommitTimestamp = timestamp,
                PackageId = id,
                PackageVersion = version.ToNormalizedString(),
                Published = timestamp,
            },
            cancellationToken);
    }

    internal static PackageDetailsCatalogLeaf BuildDetailsLeaf(
        Package package,
        string hash,
        long size,
        string commitId,
        DateTimeOffset timestamp)
    {
        var published = new DateTimeOffset(DateTime.SpecifyKind(package.Published, DateTimeKind.Utc));

        return new PackageDetailsCatalogLeaf
        {
            Type = new[] { CatalogEntry.PackageDetailsType, "catalog:Permalink" },
            CommitId = commitId,
            CommitTimestamp = timestamp,
            PackageId = package.Id,
            PackageVersion = package.NormalizedVersionString,
            VerbatimVersion = package.OriginalVersionString ?? package.NormalizedVersionString,
            Published = published,
            Created = published,
            LastEdited = timestamp,
            Authors = string.Join(", ", package.Authors ?? Array.Empty<string>()),
            Description = package.Description,
            DependencyGroups = RegistrationBuilder.ToDependencyGroups(package).ToList(),
            IconUrl = NullIfEmpty(package.IconUrlString),
            IsPrerelease = package.IsPrerelease,
            Language = NullIfEmpty(package.Language),
            LicenseUrl = NullIfEmpty(package.LicenseUrlString),
            Listed = package.Listed,
            MinClientVersion = NullIfEmpty(package.MinClientVersion),
            PackageHash = hash,
            PackageHashAlgorithm = "SHA512",
            PackageSize = size,
            ProjectUrl = NullIfEmpty(package.ProjectUrlString),
            ReleaseNotes = NullIfEmpty(package.ReleaseNotes),
            RequireLicenseAcceptance = package.RequireLicenseAcceptance,
            Summary = NullIfEmpty(package.Summary),
            Tags = package.Tags?.ToList(),
            Title = NullIfEmpty(package.Title),
        };
    }

    /// <summary>
    /// The next commit timestamp: now, truncated to milliseconds so that every database
    /// provider stores it exactly, and always later than the previous commit.
    /// </summary>
    internal static DateTime NextTimestamp(DateTime now, DateTime? last)
    {
        now = now.ToUniversalTime();
        var truncated = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

        if (last.HasValue && truncated <= last.Value)
        {
            return last.Value.AddMilliseconds(1);
        }

        return truncated;
    }

    private async Task CommitAsync(
        string type,
        string? id,
        NuGetVersion version,
        Func<string, DateTimeOffset, CatalogLeaf> buildLeaf,
        CancellationToken cancellationToken)
    {
        await CommitLock.WaitAsync(cancellationToken);
        IAsyncDisposable? leaseHandle = null;
        try
        {
            leaseHandle = await AcquireLeaseAsync(cancellationToken);

            // Before the leaf is written: a closed database would refuse the entry and leave the leaf orphaned.
            _gate?.ThrowIfClosed();

            var last = await _catalog.GetLastCommitTimestampAsync(cancellationToken);
            var timestamp = NextTimestamp(_time.UtcNow, last);
            var commitId = Guid.NewGuid().ToString("D");

            var leaf = buildLeaf(commitId, new DateTimeOffset(timestamp));

            using (var content = new MemoryStream())
            {
                await JsonSerializer.SerializeAsync(content, leaf, leaf.GetType(), LeafSerializerOptions, cancellationToken);
                content.Position = 0;

                var result = await _storage.PutAsync(LeafPath(_repository?.Current ?? RepositoryInfo.Default, commitId), content, "application/json", cancellationToken);
                if (result == StoragePutResult.Conflict)
                {
                    throw new InvalidOperationException($"Catalog leaf {commitId} already exists with different content");
                }
            }

            await _catalog.AddAsync(
                new CatalogEntry
                {
                    CommitId = commitId,
                    CommitTimestamp = timestamp,
                    Type = type,
                    PackageId = id,
                    PackageVersion = version.ToNormalizedString(),
                },
                cancellationToken);

            _logger.LogInformation(
                "Committed catalog {Type} entry {CommitId} for {PackageId} {PackageVersion}",
                type,
                commitId,
                id,
                version);
        }
        finally
        {
            if (leaseHandle != null) await leaseHandle.DisposeAsync();
            CommitLock.Release();
        }
    }

    // Waits for the repository's commit lease: a timestamp is "last entry + 1" and two nodes reading the same last
    // entry would give the later key an equal or earlier timestamp, which a catalog consumer's cursor skips for good.
    private async Task<IAsyncDisposable?> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        if (_lease == null) return null;

        var job = "catalog:" + (_repository?.Current ?? RepositoryInfo.Default).Key;
        var deadline = DateTime.UtcNow + LeaseWait;
        while (true)
        {
            var handle = await _lease.TryAcquireAsync(job, cancellationToken);
            if (handle != null) return handle;

            // A store that cannot be reached is not another node holding the lease: waiting would block every commit
            // (and, through the lock, every push) for the whole wait and then lose the entry. Commit under the local lock.
            if (_lease is IJobLeaseAvailability availability && !await availability.IsAvailableAsync(cancellationToken))
            {
                _logger.LogWarning("The lease store is unreachable; committing catalog entry for '{Job}' without the cross-node lease.", job);
                return null;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"The catalog commit lease '{job}' stayed with another node for {LeaseWait.TotalSeconds:0} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }

    private async Task<(string Hash, long Size)> HashPackageAsync(Package package, CancellationToken cancellationToken)
    {
        using var stream = await _packageStorage.GetPackageStreamAsync(package.Id, package.Version, cancellationToken);
        using var counting = new CountingStream(stream);
        using var sha512 = SHA512.Create();

        var hash = await sha512.ComputeHashAsync(counting, cancellationToken);

        return (Convert.ToBase64String(hash), counting.BytesRead);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
