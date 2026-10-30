using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// The cluster-wide <see cref="IJobLease"/>: a key in the shared store that only one node can create, holding the holder's name. The
/// holder renews it every third of <see cref="ClusterRuntime.LeaseDuration"/>, so a node that dies frees its jobs within that
/// time, and a lease that could not be renewed is reported (the job itself is not interrupted). When the store cannot be reached the
/// lease is refused, so a job is skipped rather than run twice.
/// </summary>
public sealed class ClusterJobLease : IJobLease, IJobLeaseAvailability
{
    private readonly ClusterRuntime _cluster;
    private readonly ILogger<ClusterJobLease> _logger;

    /// <summary>Creates the lease over the cluster's store.</summary>
    public ClusterJobLease(ClusterRuntime cluster, ILogger<ClusterJobLease> logger)
    {
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (_cluster.Store == null) throw new InvalidOperationException("ClusterJobLease needs Ha:Enabled and a cluster store.");
    }

    /// <inheritdoc/>
    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(string job, CancellationToken cancellationToken)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));

        var store = _cluster.Store!;
        var key = ClusterRuntime.Keys.Lease(job);
        var value = LeaseInfo.Format(_cluster.NodeId, Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        try
        {
            if (!await store.SetIfAbsentAsync(key, value, _cluster.LeaseDuration, cancellationToken).ConfigureAwait(false)) return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Could not take the lease {Job}; the job is skipped.", job);

            return null;
        }

        return new Held(store, key, value, job, _cluster.LeaseDuration, _logger);
    }

    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _cluster.Store!.PingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }

    private sealed class Held : IAsyncDisposable
    {
        private readonly IClusterStore _store;
        private readonly string _key;
        private readonly string _value;
        private readonly string _job;
        private readonly TimeSpan _duration;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _renewing;
        private int _disposed;

        public Held(IClusterStore store, string key, string value, string job, TimeSpan duration, ILogger logger)
        {
            _store = store;
            _key = key;
            _value = value;
            _job = job;
            _duration = duration;
            _logger = logger;
            _renewing = Task.Run(RenewAsync);
        }

        private async Task RenewAsync()
        {
            var every = TimeSpan.FromTicks(_duration.Ticks / 3);
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    await Task.Delay(every, _stop.Token).ConfigureAwait(false);
                    try
                    {
                        if (!await _store.ExtendIfValueAsync(_key, _value, _duration, _stop.Token).ConfigureAwait(false))
                        {
                            _logger.LogWarning("The lease {Job} was lost (it expired or was taken); the job may now run on two nodes.", _job);

                            return;
                        }
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _logger.LogWarning(e, "The lease {Job} could not be renewed; it is retried.", _job);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            _stop.Cancel();
            try
            {
                await _renewing.ConfigureAwait(false);
                await _store.DeleteIfValueAsync(_key, _value, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // The key expires by itself.
                _logger.LogWarning(e, "Could not release the lease {Job}; it expires by itself.", _job);
            }
            finally
            {
                _stop.Dispose();
            }
        }
    }
}

/// <summary>A lease as stored: the holder's node, a token that tells two holders of one node apart, and when it was taken.</summary>
/// <param name="Job">The job's name.</param>
/// <param name="Node">The holder.</param>
/// <param name="Since">When it was acquired (UTC).</param>
public sealed record LeaseInfo(string Job, string Node, DateTime Since)
{
    internal static string Format(string node, string token, DateTime since)
        => node + "|" + token + "|" + since.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    internal static LeaseInfo? Parse(string job, string value)
    {
        var parts = value.Split('|');
        if (parts.Length < 3) return null;

        return DateTime.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var since)
            ? new LeaseInfo(job, parts[0], since)
            : new LeaseInfo(job, parts[0], DateTime.MinValue);
    }
}
