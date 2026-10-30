using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// Remembers when each version was last downloaded, for the retention rules "unused for N days" and the remote cache limit. A download
/// only touches an in-memory map (no database write on the download path); <see cref="FlushAsync"/> (the retention worker, every
/// minute, and the engine before it plans) writes the newest time of each version in batches. A restart loses at most the last minute,
/// and a closed <see cref="MaintenanceGate"/> keeps the entries until it opens. Names and versions are compared case-insensitively.
/// </summary>
public sealed class ArtifactUsageRecorder
{
    /// <summary>The most distinct versions remembered between two flushes; further new ones are dropped (known ones keep updating).</summary>
    public const int MaxPending = 50_000;

    private const int Chunk = 400;

    private readonly ConcurrentDictionary<(int Repository, string Name, string Version), DateTime> _pending = new();
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly SystemTime _time;
    private readonly ILogger<ArtifactUsageRecorder>? _logger;

    public ArtifactUsageRecorder(SystemTime time, ILogger<ArtifactUsageRecorder>? logger = null)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger;
    }

    /// <summary>Versions waiting to be written.</summary>
    public int Pending => _pending.Count;

    /// <summary>The key of a version in the map and in the database: lower case, so the engines' collations agree.</summary>
    internal static string Normalize(string value) => value.ToLowerInvariant();

    /// <summary>Notes that <paramref name="name"/> <paramref name="version"/> of repository <paramref name="repositoryKey"/> was downloaded now.</summary>
    public void Touch(int repositoryKey, string? name, string? version)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version)) return;
        if (name.Length > RetentionLimits.MaxNameLength || version.Length > RetentionLimits.MaxVersionLength) return;

        var key = (repositoryKey, Normalize(name), Normalize(version));
        var now = _time.UtcNow;
        if (_pending.Count >= MaxPending && !_pending.ContainsKey(key)) return;

        _pending.AddOrUpdate(key, now, (_, old) => old > now ? old : now);
    }

    /// <summary>
    /// Writes the pending times. Entries that cannot be written (the gate is closed, the database is unreachable) stay for the next
    /// flush. Never throws except for cancellation.
    /// </summary>
    /// <returns>How many versions were written.</returns>
    public async Task<int> FlushAsync(IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        if (scopes == null) throw new ArgumentNullException(nameof(scopes));
        if (_pending.IsEmpty) return 0;

        await _flushing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = _pending.ToArray();
            if (snapshot.Length == 0) return 0;

            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<IContext>();
            if (context is NullContext)
            {
                _pending.Clear();

                return 0;
            }

            var written = 0;
            foreach (var group in snapshot.GroupBy(e => e.Key.Repository))
            {
                foreach (var batch in group.Chunk(Chunk))
                {
                    try
                    {
                        await WriteAsync(context, group.Key, batch, cancellationToken).ConfigureAwait(false);
                        foreach (var entry in batch) ((ICollection<KeyValuePair<(int, string, string), DateTime>>)_pending).Remove(entry);
                        written += batch.Length;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (MaintenanceException)
                    {
                        return written;
                    }
                    catch (Exception e)
                    {
                        context = scope.ServiceProvider.GetRequiredService<IContext>();

                        // A repository that was deleted meanwhile (RepositoryKey is a foreign key): its times belong to nothing, drop them.
                        if (!await RepositoryExistsAsync(context, group.Key, cancellationToken).ConfigureAwait(false))
                        {
                            foreach (var entry in group) ((ICollection<KeyValuePair<(int, string, string), DateTime>>)_pending).Remove(entry);
                            break;
                        }

                        // A race with another node: keep the entries, try again.
                        _logger?.LogWarning(e, "Could not record download times for repository {Repository}", group.Key);
                    }
                }
            }

            return written;
        }
        finally
        {
            _flushing.Release();
        }
    }

    // False only when the database says so; a failing lookup counts as "exists" (keep the entries).
    private static async Task<bool> RepositoryExistsAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Repositories!.AsNoTracking().AnyAsync(r => r.Key == repositoryKey, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static async Task WriteAsync(IContext context, int repositoryKey, KeyValuePair<(int Repository, string Name, string Version), DateTime>[] batch, CancellationToken cancellationToken)
    {
        var names = batch.Select(e => e.Key.Name).Distinct().ToList();
        var rows = await context.ArtifactUsages!.IgnoreQueryFilters()
            .Where(u => u.RepositoryKey == repositoryKey && names.Contains(u.Name!))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existing = rows.ToDictionary(r => (r.Name!, r.Version!), StringTupleComparer.Instance);

        foreach (var entry in batch)
        {
            if (existing.TryGetValue((entry.Key.Name, entry.Key.Version), out var row))
            {
                if (entry.Value > row.LastUsed) row.LastUsed = entry.Value;
            }
            else
            {
                context.ArtifactUsages!.Add(new ArtifactUsage { RepositoryKey = repositoryKey, Name = entry.Key.Name, Version = entry.Key.Version, LastUsed = entry.Value });
            }
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A failed save must not leave rejected rows tracked for the next batch.
            if (context is Microsoft.EntityFrameworkCore.DbContext db) db.ChangeTracker.Clear();
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item2));
    }
}

/// <summary>The recorded download times of the repository of a context (its query filter applies), keyed by name and version.</summary>
public sealed class ArtifactUsageMap
{
    private readonly Dictionary<(string, string), DateTime> _times;

    private ArtifactUsageMap(Dictionary<(string, string), DateTime> times)
    {
        _times = times;
    }

    /// <summary>The map of every version of the context's repository.</summary>
    public static async Task<ArtifactUsageMap> LoadAsync(IContext context, CancellationToken cancellationToken)
    {
        var rows = await context.ArtifactUsages!.AsNoTracking().Select(u => new { u.Name, u.Version, u.LastUsed }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var times = new Dictionary<(string, string), DateTime>();
        foreach (var row in rows)
        {
            var key = (ArtifactUsageRecorder.Normalize(row.Name!), ArtifactUsageRecorder.Normalize(row.Version!));
            var value = DateTime.SpecifyKind(row.LastUsed, DateTimeKind.Utc);
            if (!times.TryGetValue(key, out var old) || value > old) times[key] = value;
        }

        return new ArtifactUsageMap(times);
    }

    /// <summary>The last download of the version, or null.</summary>
    public DateTime? Get(string name, string version)
        => _times.TryGetValue((ArtifactUsageRecorder.Normalize(name), ArtifactUsageRecorder.Normalize(version)), out var time) ? time : null;

    /// <summary>The later of the two (a Docker tag and the digest it points at).</summary>
    public DateTime? Latest(string name, string version, string otherVersion)
    {
        var a = Get(name, version);
        var b = Get(name, otherVersion);

        return a == null ? b : b == null ? a : (a > b ? a : b);
    }
}
