using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGet.Ha.Tests;

/// <summary>
/// The Redis <see cref="IClusterStore"/> against a real Redis (skips without Docker): the store's contract, a lease between two
/// connections, and two BaGet nodes in one process coordinating through it (the read-only gate and the jobs that run once).
/// </summary>
public class RedisClusterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<RedisClusterStore> StoreAsync(string? prefix = null)
        => new(await RedisContainer.ConnectionStringAsync(), prefix ?? "test" + Guid.NewGuid().ToString("N").Substring(0, 8));

    [Fact]
    public async Task TheStoreKeepsValuesWithTimeToLiveAndPrefix()
    {
        await using var store = await StoreAsync();

        Assert.True(await store.PingAsync(Token));
        Assert.True(await store.SetIfAbsentAsync("k", "a", TimeSpan.FromSeconds(30), Token));
        Assert.False(await store.SetIfAbsentAsync("k", "b", TimeSpan.FromSeconds(30), Token));
        Assert.Equal("a", await store.GetAsync("k", Token));

        await store.SetAsync("short", "x", TimeSpan.FromMilliseconds(300), Token);
        await Task.Delay(TimeSpan.FromMilliseconds(600), Token);
        Assert.Null(await store.GetAsync("short", Token));

        Assert.True(await store.DeleteAsync("k", Token));
        Assert.False(await store.DeleteAsync("k", Token));
    }

    [Fact]
    public async Task ExtendAndReleaseWorkOnlyForTheHolderOfTheValue()
    {
        await using var store = await StoreAsync();
        await store.SetAsync("lease", "mine", TimeSpan.FromSeconds(30), Token);

        Assert.False(await store.ExtendIfValueAsync("lease", "other", TimeSpan.FromSeconds(60), Token));
        Assert.False(await store.DeleteIfValueAsync("lease", "other", Token));
        Assert.True(await store.ExtendIfValueAsync("lease", "mine", TimeSpan.FromSeconds(60), Token));
        Assert.True(await store.DeleteIfValueAsync("lease", "mine", Token));
        Assert.Null(await store.GetAsync("lease", Token));
    }

    [Fact]
    public async Task ACounterStartsItsWindowAtTheFirstIncrementAndListReturnsKeysWithoutThePrefix()
    {
        await using var store = await StoreAsync();

        Assert.Equal(1, await store.IncrementAsync("c", TimeSpan.FromMilliseconds(700), Token));
        Assert.Equal(2, await store.IncrementAsync("c", TimeSpan.FromMilliseconds(700), Token));
        await store.SetAsync("node:a", "1", null, Token);
        await store.SetAsync("node:b", "2", null, Token);
        await store.SetAsync("lease:x", "3", null, Token);

        var nodes = await store.ListAsync("node:", Token);
        Assert.Equal(new[] { "node:a", "node:b" }, nodes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal("2", nodes["node:b"]);

        await Task.Delay(TimeSpan.FromMilliseconds(900), Token);
        Assert.Equal(1, await store.IncrementAsync("c", TimeSpan.FromMilliseconds(700), Token));
    }

    [Fact]
    public async Task TwoClustersWithDifferentPrefixesDoNotSeeEachOther()
    {
        await using var a = await StoreAsync("cluster-a");
        await using var b = await StoreAsync("cluster-b");
        await a.SetAsync("gate", "closed", TimeSpan.FromSeconds(30), Token);

        Assert.Null(await b.GetAsync("gate", Token));
        Assert.Empty(await b.ListAsync("gate", Token));
    }

    [Fact]
    public async Task ALeaseOverRedisIsExclusiveBetweenTwoConnectionsAndRenewedWhileHeld()
    {
        var connection = await RedisContainer.ConnectionStringAsync();
        var prefix = "lease" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var storeOne = new RedisClusterStore(connection, prefix);
        await using var storeTwo = new RedisClusterStore(connection, prefix);
        var duration = TimeSpan.FromSeconds(3);
        ClusterRuntime Runtime(string name, IClusterStore store) => new(Options.Create(new HaOptions { Enabled = true, NodeName = name, LeaseDuration = duration, ConnectionString = "x" }), store);
        var one = new ClusterJobLease(Runtime("one", storeOne), NullLogger<ClusterJobLease>.Instance);
        var two = new ClusterJobLease(Runtime("two", storeTwo), NullLogger<ClusterJobLease>.Instance);

        await using (var held = await one.TryAcquireAsync("replication:1", Token))
        {
            Assert.NotNull(held);

            // Longer than the lease lasts: only the renewals keep it.
            await Task.Delay(TimeSpan.FromSeconds(7), Token);
            Assert.Null(await two.TryAcquireAsync("replication:1", Token));
            Assert.Contains("one|", (await storeTwo.ListAsync("lease:", Token))["lease:replication:1"]);
        }

        await using var second = await two.TryAcquireAsync("replication:1", Token);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task TwoNodesCoordinateThroughRedisTheGateIsSharedAndAJobRunsOnce()
    {
        var connection = await RedisContainer.ConnectionStringAsync();
        var prefix = "nodes" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var store = new RedisClusterStore(connection, prefix);
        var output = TestContext.Current.TestOutputHelper!;
        var operation = new BlockingOperation();
        using var cluster = new BaGet.Tests.ClusterHarness(output, store, services => services.AddSingleton<IAdminOperation>(operation));
        var gateOne = cluster.One.Services.GetRequiredService<MaintenanceGate>();
        var gateTwo = cluster.Two.Services.GetRequiredService<MaintenanceGate>();

        gateOne.Close("Copying the database");
        await cluster.SyncAsync();
        Assert.True(gateTwo.IsClosed);
        Assert.True(gateOne.Open());
        await cluster.SyncAsync();
        Assert.False(gateTwo.IsClosed);

        var repository = RepositoryInfo.Default;
        var jobsOne = cluster.One.Services.GetRequiredService<AdminJobService>();
        var jobsTwo = cluster.Two.Services.GetRequiredService<AdminJobService>();
        Assert.True(jobsOne.TryStart("blocking-test", repository, "root", out _));
        Assert.False(jobsTwo.TryStart("blocking-test", repository, "root", out var refusal));
        Assert.Contains("another node", refusal);

        var nodes = await store.ListAsync(ClusterRuntime.Keys.NodePrefix, Token);
        Assert.Equal(2, nodes.Count);

        operation.Release.SetResult(true);
        await jobsOne.WaitAsync("blocking-test", repository.Name);
    }

    private sealed class BlockingOperation : IAdminOperation
    {
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "blocking-test";

        public string Title => "Blocking test";

        public string Description => "Waits until released.";

        public bool IsAvailable(IServiceProvider services) => true;

        public async Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            await Release.Task.WaitAsync(cancellationToken);

            return "done";
        }
    }
}
