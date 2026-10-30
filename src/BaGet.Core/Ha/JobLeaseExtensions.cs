using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// How the background workers use <see cref="IJobLease"/>. A worker built without a lease (a test, an embedded host) runs
/// everything, as a single node does; with one it runs only what it holds the lease for, so a job runs once cluster-wide.
/// </summary>
public static class JobLeaseExtensions
{
    private static readonly IAsyncDisposable Nothing = new NoLease();

    /// <summary>
    /// Takes the lease for <paramref name="job"/>, or returns a no-op handle when there is no lease service.
    /// </summary>
    /// <returns>A handle to dispose when the job ends, or null when another holder has the lease (skip the job).</returns>
    public static async ValueTask<IAsyncDisposable?> AcquireAsync(this IJobLease? lease, string job, CancellationToken cancellationToken)
        => lease == null ? Nothing : await lease.TryAcquireAsync(job, cancellationToken).ConfigureAwait(false);

    /// <summary>Blocking form for the synchronous <c>TryStart</c> methods (one store round trip).</summary>
    public static IAsyncDisposable? Acquire(this IJobLease? lease, string job)
        => lease == null ? Nothing : lease.TryAcquireAsync(job, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <summary>Releases a handle from <see cref="Acquire"/> synchronously.</summary>
    public static void Release(this IAsyncDisposable handle) => handle.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private sealed class NoLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => default;
    }
}
