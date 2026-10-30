using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// The upstream a repository fetches from, as <see cref="IRemoteUpstreamSource"/> resolved it.
/// </summary>
/// <param name="Key">The key of the repository that owns the settings and the cache: the remote repository itself, or for the default repository's mirror the <c>mirror</c> repository (0 when it has none, the settings then coming from <c>Mirror:*</c>).</param>
/// <param name="Name">That repository's name.</param>
/// <param name="Settings">The upstream's settings, password in clear (memory only).</param>
public sealed record ResolvedUpstream(int Key, string Name, RemoteRepositorySettings Settings);

/// <summary>
/// Decides which upstream, if any, serves a repository. The seam of D3 in the Artifactory plan: a remote repository
/// is its own upstream; the default repository uses the <c>mirror</c> remote repository while <c>Mirror:Enabled</c>
/// is set; when virtual repositories arrive, a virtual one resolves its members here.
/// </summary>
public interface IRemoteUpstreamSource
{
    /// <summary>The upstream of <paramref name="repository"/>, or null when it has none (a local repository).</summary>
    Task<ResolvedUpstream?> ResolveAsync(RepositoryInfo repository, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IRemoteUpstreamSource"/> over the repositories table and the <c>Mirror:*</c> options.
/// </summary>
public sealed class RemoteUpstreamSource : IRemoteUpstreamSource
{
    private sealed record MirrorRow(int Key);

    private readonly IContext _context;
    private readonly RemoteSettingsCodec _codec;
    private readonly RemoteRepositoryState _state;
    private readonly IOptions<MirrorOptions> _mirror;

    public RemoteUpstreamSource(
        IContext context,
        RemoteSettingsCodec codec,
        RemoteRepositoryState state,
        IOptions<MirrorOptions> mirror)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
    }

    /// <inheritdoc/>
    public async Task<ResolvedUpstream?> ResolveAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));

        if (repository.Kind == RepositoryKind.Remote)
        {
            var settings = await LoadAsync(repository.Key, cancellationToken).ConfigureAwait(false);

            return settings == null ? null : new ResolvedUpstream(repository.Key, repository.Name, settings);
        }

        if (!repository.IsDefault) return null;

        var mirror = _mirror.Value;
        if (!mirror.Enabled || mirror.PackageSource == null) return null;

        var row = await _state.GetCachedAsync(
            RemoteRepositoryState.MirrorLookupKey,
            async () => await FindMirrorRowAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var fromRow = row == null ? null : await LoadAsync(row.Key, cancellationToken).ConfigureAwait(false);
        if (row == null || fromRow == null)
        {
            return new ResolvedUpstream(0, Repository.MirrorName, RemoteRepositorySettings.FromMirror(mirror));
        }

        // Mirror:* stays authoritative for where the upstream is; the repository holds the rest (credentials, cache
        // periods, offline). It is restart-required configuration, so this only ever changes with a restart.
        var settings2 = fromRow.Clone();
        settings2.Url = mirror.PackageSource.AbsoluteUri;
        settings2.Protocol = mirror.Legacy ? RemoteProtocol.V2 : RemoteProtocol.V3;
        settings2.DownloadTimeoutSeconds = mirror.PackageDownloadTimeoutSeconds;

        return new ResolvedUpstream(row.Key, Repository.MirrorName, settings2);
    }

    private async Task<MirrorRow?> FindMirrorRowAsync(CancellationToken cancellationToken)
    {
        if (_context is NullContext) return null;

        var key = await _context.Repositories!.AsNoTracking()
            .Where(r => r.Name == Repository.MirrorName && r.Kind == RepositoryKind.Remote && r.Format == RepositoryFormat.NuGet)
            .Select(r => (int?)r.Key)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return key == null ? null : new MirrorRow(key.Value);
    }

    private async Task<RemoteRepositorySettings?> LoadAsync(int key, CancellationToken cancellationToken)
    {
        if (_context is NullContext) return null;

        return await _state.GetCachedAsync(key, async () =>
        {
            var json = await _context.Repositories!.AsNoTracking()
                .Where(r => r.Key == key && r.Kind == RepositoryKind.Remote)
                .Select(r => r.Settings)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var settings = _codec.Deserialize(json);

            return settings != null && settings.Validate().Count == 0 ? settings : null;
        }).ConfigureAwait(false);
    }
}

/// <summary>
/// An <see cref="IUpstreamClient"/> that is also told how the repository treats what it fetches.
/// </summary>
public interface IPassThroughUpstreamClient : IUpstreamClient
{
    /// <summary>
    /// True when the repository does not keep what it fetches (<see cref="RemoteRepositorySettings.StoreLocally"/>
    /// off): the package service then answers from the upstream directly and streams downloads through.
    /// </summary>
    Task<bool> IsPassThroughAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The <see cref="IUpstreamClient"/> of the current repository (<see cref="IRepositoryContext"/>): resolves its
/// upstream, honours offline mode, reuses answers for the metadata period, remembers 404s for the negative period, and
/// serves an expired answer when the upstream cannot be reached. A repository without an upstream gets empty answers,
/// like <see cref="DisabledUpstreamClient"/>.
/// </summary>
/// <remarks>
/// A failure to reach the upstream is never cached as "not found" (only a 404 is), and is recorded in the repository's
/// <see cref="RemoteCacheStatus"/>. It is logged without the URL's query or any credential.
/// </remarks>
public sealed class RemoteUpstreamClient : IPassThroughUpstreamClient
{
    private static readonly IReadOnlyList<NuGetVersion> NoVersions = Array.Empty<NuGetVersion>();
    private static readonly IReadOnlyList<Package> NoPackages = Array.Empty<Package>();

    private readonly IRepositoryContext? _repository;
    private readonly IRemoteUpstreamSource _source;
    private readonly RemoteRepositoryState _state;
    private readonly IRemoteHttpClientFactory _http;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<RemoteUpstreamClient> _logger;

    public RemoteUpstreamClient(
        IRepositoryContext? repository,
        IRemoteUpstreamSource source,
        RemoteRepositoryState state,
        IRemoteHttpClientFactory http,
        ILoggerFactory loggers)
    {
        _repository = repository;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
        _logger = loggers.CreateLogger<RemoteUpstreamClient>();
    }

    private Task<ResolvedUpstream?> ResolveAsync(CancellationToken cancellationToken)
        => _source.ResolveAsync(_repository?.Current ?? RepositoryInfo.Default, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> IsPassThroughAsync(CancellationToken cancellationToken)
    {
        var upstream = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return upstream != null && !upstream.Settings.StoreLocally;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        var upstream = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return NoVersions;

        return await CachedAsync(
            upstream,
            "v:" + id.ToLowerInvariant(),
            NoVersions,
            ct => Inner(upstream, out var dispose).WithDispose(dispose, c => c.ListPackageVersionsAsync(id, ct)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken)
    {
        var upstream = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return NoPackages;

        return await CachedAsync(
            upstream,
            "p:" + id.ToLowerInvariant(),
            NoPackages,
            ct => Inner(upstream, out var dispose).WithDispose(dispose, c => c.ListPackagesAsync(id, ct)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Stream?> DownloadPackageOrNullAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
    {
        var upstream = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return null;

        var key = upstream.Key;
        var cacheKey = DownloadCacheKey(id, version);
        if (DownloadGate(upstream, cacheKey) != Gate.Ask) return null;

        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);

        try
        {
            var stream = await Inner(upstream, out var dispose)
                .WithDispose(dispose, c => c.DownloadPackageOrNullAsync(id, version, cancellationToken))
                .ConfigureAwait(false);
            _state.Succeeded(key);

            if (stream == null)
            {
                _state.Set(key, cacheKey, null, negative: true, TimeSpan.FromSeconds(upstream.Settings.NegativeTtlSeconds));
            }

            return stream;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Fail(upstream, e);

            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Applies the gate a download applies (offline, remembered "not found"), then asks the version list.</remarks>
    public async Task<UpstreamProbe> ProbeAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        var upstream = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (upstream == null) return UpstreamProbe.Absent;

        switch (DownloadGate(upstream, DownloadCacheKey(id, version)))
        {
            case Gate.Offline: return UpstreamProbe.Offline;
            case Gate.KnownAbsent: return UpstreamProbe.Absent;
        }

        // The list answers "nothing" when the upstream fails and counts the failure, which is how a down upstream is
        // told from one that simply does not have the package.
        var failuresBefore = _state.Status(upstream.Key).Failures;
        var versions = await ListPackageVersionsAsync(id, cancellationToken).ConfigureAwait(false);
        if (versions.Contains(version)) return UpstreamProbe.Available;

        return _state.Status(upstream.Key).Failures > failuresBefore ? UpstreamProbe.Unavailable : UpstreamProbe.Absent;
    }

    private enum Gate
    {
        Ask,
        Offline,
        KnownAbsent,
    }

    private static string DownloadCacheKey(string? id, NuGetVersion version)
        => "d:" + (id ?? string.Empty).ToLowerInvariant() + "/" + version.ToNormalizedString().ToLowerInvariant();

    // Whether a download is asked of the upstream at all: never offline, not while a fresh 404 is remembered. Download and
    // Probe share it so the preview cannot disagree with a real request.
    private Gate DownloadGate(ResolvedUpstream upstream, string cacheKey)
    {
        if (upstream.Settings.Offline) return Gate.Offline;

        if (_state.TryGet(upstream.Key, cacheKey, out _, out var negative, out var fresh) && negative && fresh)
        {
            _state.Count(upstream.Key, RemoteRepositoryState.CacheEvent.NegativeHit);

            return Gate.KnownAbsent;
        }

        return Gate.Ask;
    }

    // The metadata period, the negative period, offline mode and stale-on-error around one question.
    private async Task<IReadOnlyList<T>> CachedAsync<T>(
        ResolvedUpstream upstream,
        string cacheKey,
        IReadOnlyList<T> none,
        Func<CancellationToken, Task<IReadOnlyList<T>>> ask,
        CancellationToken cancellationToken)
    {
        var key = upstream.Key;
        var settings = upstream.Settings;
        var cached = _state.TryGet(key, cacheKey, out var value, out var negative, out var fresh);

        // Offline: whatever is remembered, however old; else nothing. The upstream is not contacted.
        if (settings.Offline)
        {
            if (cached && !negative && value is IReadOnlyList<T> remembered)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return remembered;
            }

            return none;
        }

        if (cached && fresh)
        {
            if (negative)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.NegativeHit);

                return none;
            }

            if (value is IReadOnlyList<T> hit)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Hit);

                return hit;
            }
        }

        _state.Count(key, RemoteRepositoryState.CacheEvent.Miss);

        try
        {
            var answer = await ask(cancellationToken).ConfigureAwait(false);
            _state.Succeeded(key);

            if (answer.Count == 0)
            {
                _state.Set(key, cacheKey, null, negative: true, TimeSpan.FromSeconds(settings.NegativeTtlSeconds));
            }
            else
            {
                _state.Set(key, cacheKey, answer, negative: false, TimeSpan.FromSeconds(settings.MetadataTtlSeconds));
            }

            return answer;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Fail(upstream, e);

            if (cached && !negative && value is IReadOnlyList<T> stale)
            {
                _state.Count(key, RemoteRepositoryState.CacheEvent.Stale);

                return stale;
            }

            return none;
        }
    }

    private void Fail(ResolvedUpstream upstream, Exception e)
    {
        var what = Describe(e);
        _state.Failed(upstream.Key, what);

        // The exception itself is not logged: its message can carry the upstream's URL with a query string.
        _logger.LogWarning(
            "The upstream of repository {Repository} could not be reached: {Error}",
            upstream.Name,
            what);
    }

    /// <summary>A description of a failure that is safe to show and log: no URL, no header, no credential.</summary>
    internal static string Describe(Exception e)
    {
        var inner = e is AggregateException { InnerException: { } a } ? a : e;
        var text = inner.GetType().Name;

        if (inner is System.Net.Http.HttpRequestException http)
        {
            if (http.StatusCode != null) return $"{text}: HTTP {(int)http.StatusCode} {http.StatusCode}";
            if (http.InnerException != null) text += " (" + http.InnerException.GetType().Name + ")";
        }
        else if (inner is TaskCanceledException or TimeoutException)
        {
            return "The upstream timed out.";
        }

        return text;
    }

    // The client of the upstream for one call. V3 shares a pooled transport (its service index is cached); a V2 client
    // owns a NuGet source cache, so it is disposed after the call.
    private IUpstreamClient Inner(ResolvedUpstream upstream, out IDisposable? dispose)
    {
        var settings = upstream.Settings;
        dispose = null;

        if (settings.Protocol == RemoteProtocol.V2)
        {
            var client = new V2UpstreamClient(
                settings.UrlOrNull!, settings.Username, settings.Password, _loggers.CreateLogger<V2UpstreamClient>(), propagateErrors: true);
            dispose = client;

            return client;
        }

        var transport = _state.GetTransport(upstream.Key, settings, () =>
        {
            var http = _http.Create(settings);

            return new UpstreamTransport(
                settings.TransportKey(),
                http,
                new NuGetClient(new NuGetClientFactory(http, settings.UrlOrNull!.AbsoluteUri)));
        });

        return new V3UpstreamClient(transport.V3!, _loggers.CreateLogger<V3UpstreamClient>(), propagateErrors: true);
    }
}

internal static class UpstreamCallExtensions
{
    // Runs a call on a client and disposes it (a V2 client) afterwards.
    public static async Task<T> WithDispose<T>(this IUpstreamClient client, IDisposable? dispose, Func<IUpstreamClient, Task<T>> call)
    {
        try
        {
            return await call(client).ConfigureAwait(false);
        }
        finally
        {
            dispose?.Dispose();
        }
    }
}
