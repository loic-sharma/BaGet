using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core;

public class PackageService : IPackageService
{
    // Serialises the first fetch of one version, so a second request waits and finds it instead of failing.
    private static readonly KeyedLocks Mirrors = new();

    private readonly IPackageDatabase _db;
    private readonly IUpstreamClient _upstream;
    private readonly IPackageIndexingService _indexer;
    private readonly ILogger<PackageService> _logger;
    private readonly IRepositoryContext? _repository;
    private readonly ArtifactUsageRecorder? _usage;

    public PackageService(
        IPackageDatabase db,
        IUpstreamClient upstream,
        IPackageIndexingService indexer,
        ILogger<PackageService> logger,
        IRepositoryContext? repository = null,
        ArtifactUsageRecorder? usage = null)
    {
        _repository = repository;
        _usage = usage;
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var upstreamVersions = await _upstream.ListPackageVersionsAsync(id, cancellationToken);

        // Merge the local package versions into the upstream package versions.
        var localPackages = await _db.FindAsync(id, includeUnlisted: true, cancellationToken);
        var localVersions = localPackages.Select(p => p.Version);

        if (!upstreamVersions.Any()) return localVersions.ToList();
        if (!localPackages.Any()) return upstreamVersions;

        return upstreamVersions.Concat(localVersions).Distinct().ToList();
    }

    public async Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken)
    {
        var upstreamPackages = await _upstream.ListPackagesAsync(id, cancellationToken);
        var localPackages = await _db.FindAsync(id, includeUnlisted: true, cancellationToken);

        if (!upstreamPackages.Any()) return localPackages;
        if (!localPackages.Any()) return upstreamPackages;

        // Merge the local packages into the upstream packages.
        var result = upstreamPackages.ToDictionary(p => p.Version);
        var local = localPackages.ToDictionary(p => p.Version);

        foreach (var localPackage in local)
        {
            result[localPackage.Key] = localPackage.Value;
        }

        return result.Values.ToList();
    }

    public async Task<Package?> FindPackageOrNullAsync(
        string? id,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        if (await IsPassThroughAsync(cancellationToken))
        {
            var stored = await _db.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
            if (stored != null || id == null) return stored;

            var upstream = await _upstream.ListPackagesAsync(id, cancellationToken);

            return upstream.FirstOrDefault(p => p.Version == version);
        }

        if (!await MirrorAsync(id, version, cancellationToken))
        {
            return null;
        }

        return await _db.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (await IsPassThroughAsync(cancellationToken))
        {
            if (await _db.ExistsAsync(id, version, cancellationToken)) return true;
            if (id == null) return false;

            return (await _upstream.ListPackageVersionsAsync(id, cancellationToken)).Contains(version);
        }

        return await MirrorAsync(id, version, cancellationToken);
    }

    public async Task<Stream?> OpenUncachedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!await IsPassThroughAsync(cancellationToken)) return null;
        if (await _db.ExistsAsync(id, version, cancellationToken)) return null;

        _logger.LogInformation(
            "Streaming package {PackageId} {PackageVersion} through from the upstream feed (not stored)",
            id,
            version);

        return await _upstream.DownloadPackageOrNullAsync(id, version, cancellationToken);
    }

    /// <summary>
    /// What a request for this version would find in this repository, without fetching or storing anything: stored, or
    /// held by its upstream (it would be fetched), or neither. The same two steps as <see cref="MirrorAsync"/> (the
    /// stored copy, then the upstream, whose own rules for offline mode and remembered answers
    /// <see cref="IUpstreamClient.ProbeAsync"/> applies), so the resolution preview of a virtual repository cannot
    /// disagree with a real download.
    /// </summary>
    public async Task<ResolutionOutcome> ProbeAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (await _db.ExistsAsync(id, version, cancellationToken).ConfigureAwait(false)) return ResolutionOutcome.Stored;

        return await _upstream.ProbeAsync(id, version, cancellationToken).ConfigureAwait(false) switch
        {
            UpstreamProbe.Available => ResolutionOutcome.WouldFetch,
            UpstreamProbe.Offline => ResolutionOutcome.Offline,
            UpstreamProbe.Unavailable => ResolutionOutcome.Unavailable,
            _ => ResolutionOutcome.NotFound,
        };
    }

    // The repository fetches from an upstream but keeps nothing (RemoteRepositorySettings.StoreLocally off).
    private async Task<bool> IsPassThroughAsync(CancellationToken cancellationToken)
        => _upstream is IPassThroughUpstreamClient passThrough
           && await passThrough.IsPassThroughAsync(cancellationToken);

    public async Task AddDownloadAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        await _db.AddDownloadAsync(packageId, version, cancellationToken);

        // When the version was last downloaded, for the retention rules (kept in memory, written in batches).
        _usage?.Touch((_repository?.Current ?? RepositoryInfo.Default).Key, packageId, version.ToNormalizedString());

        BaGetDiagnostics.Downloads.Add(1);
    }

    /// <summary>
    /// Index the package from an upstream if it does not exist locally.
    /// </summary>
    /// <param name="id">The package ID to index from an upstream.</param>
    /// <param name="version">The package version to index from an upstream.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>True if the package exists locally or was indexed from an upstream source.</returns>
    private async Task<bool> MirrorAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (await _db.ExistsAsync(id, version, cancellationToken))
        {
            return true;
        }

        var key = $"{(_repository?.Current ?? RepositoryInfo.Default).Key}|{id?.ToLowerInvariant()}|{version.ToNormalizedString()}";

        using (await Mirrors.LockAsync(key, cancellationToken))
        {
            // A request that held the lock before us may have indexed it already.
            if (await _db.ExistsAsync(id, version, cancellationToken))
            {
                return true;
            }

            return await MirrorLockedAsync(id, version, cancellationToken);
        }
    }

    private async Task<bool> MirrorLockedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Package {PackageId} {PackageVersion} does not exist locally. Checking upstream feed...",
            id,
            version);

        using var activity = BaGetDiagnostics.Source.StartActivity(BaGetDiagnostics.Spans.MirrorFetch);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageId, id);
        activity?.SetTag(BaGetDiagnostics.Tags.PackageVersion, version.ToNormalizedString());

        try
        {
            using (var packageStream = await _upstream.DownloadPackageOrNullAsync(id, version, cancellationToken))
            {
                if (packageStream == null)
                {
                    activity?.SetTag(BaGetDiagnostics.Tags.Result, "NotFound");
                    _logger.LogWarning(
                        "Upstream feed does not have package {PackageId} {PackageVersion}",
                        id,
                        version);
                    return false;
                }

                _logger.LogInformation(
                    "Downloaded package {PackageId} {PackageVersion}, indexing...",
                    id,
                    version);

                var result = await _indexer.IndexAsync(packageStream, cancellationToken);

                _logger.LogInformation(
                    "Finished indexing package {PackageId} {PackageVersion} from upstream feed with result {Result}",
                    id,
                    version,
                    result);

                activity?.SetTag(BaGetDiagnostics.Tags.Result, result.ToString());

                // Another node (or a push) may have stored it between our check and the index: it exists, so serve it.
                return result == PackageIndexingResult.Success
                    || (result == PackageIndexingResult.PackageAlreadyExists
                        && await _db.ExistsAsync(id, version, cancellationToken));
            }
        }
        catch (Exception e) when (e is not MaintenanceException)
        {
            // A closed MaintenanceGate is not a failed fetch: it propagates so the caller answers 503 + Retry-After
            // (and a virtual repository does not fall through to its next member).
            BaGetDiagnostics.Fail(activity, e);

            _logger.LogError(
                e,
                "Failed to index package {PackageId} {PackageVersion} from upstream",
                id,
                version);

            return false;
        }
    }
}