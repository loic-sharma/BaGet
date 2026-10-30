using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGet.Core.Tests.Ha;

/// <summary>The in-memory cluster store, the cluster-wide job lease over it, the backend rules and the HA options (P10).</summary>
public class ClusterStoreAndLeaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static ClusterRuntime Node(string name, IClusterStore store, TimeSpan? lease = null)
        => new(Options.Create(new HaOptions { Enabled = true, NodeName = name, LeaseDuration = lease ?? TimeSpan.FromSeconds(30), ConnectionString = "x" }), store);

    // ---- store ----

    [Fact]
    public async Task AKeyExpiresAfterItsTimeToLiveAndANewValueCanThenBeSetIfAbsent()
    {
        var clock = new Clock();
        var store = new InMemoryClusterStore(() => clock.Now);

        Assert.True(await store.SetIfAbsentAsync("k", "a", TimeSpan.FromSeconds(10), Token));
        Assert.False(await store.SetIfAbsentAsync("k", "b", TimeSpan.FromSeconds(10), Token));
        Assert.Equal("a", await store.GetAsync("k", Token));

        clock.Now += TimeSpan.FromSeconds(11);

        Assert.Null(await store.GetAsync("k", Token));
        Assert.True(await store.SetIfAbsentAsync("k", "b", TimeSpan.FromSeconds(10), Token));
        Assert.Equal("b", await store.GetAsync("k", Token));
    }

    [Fact]
    public async Task ExtendAndDeleteIfValueActOnlyForTheCurrentValue()
    {
        var clock = new Clock();
        var store = new InMemoryClusterStore(() => clock.Now);
        await store.SetAsync("k", "mine", TimeSpan.FromSeconds(10), Token);

        Assert.False(await store.ExtendIfValueAsync("k", "other", TimeSpan.FromSeconds(60), Token));
        Assert.False(await store.DeleteIfValueAsync("k", "other", Token));
        Assert.True(await store.ExtendIfValueAsync("k", "mine", TimeSpan.FromSeconds(60), Token));

        clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal("mine", await store.GetAsync("k", Token));

        Assert.True(await store.DeleteIfValueAsync("k", "mine", Token));
        Assert.Null(await store.GetAsync("k", Token));
    }

    [Fact]
    public async Task ACounterCountsAndItsWindowStartsAtTheFirstIncrement()
    {
        var clock = new Clock();
        var store = new InMemoryClusterStore(() => clock.Now);

        Assert.Equal(1, await store.IncrementAsync("c", TimeSpan.FromMinutes(10), Token));
        clock.Now += TimeSpan.FromMinutes(6);
        Assert.Equal(2, await store.IncrementAsync("c", TimeSpan.FromMinutes(10), Token));

        // The second call did not move the expiry: the window that began 6 + 5 minutes ago is over.
        clock.Now += TimeSpan.FromMinutes(5);

        Assert.Equal(1, await store.IncrementAsync("c", TimeSpan.FromMinutes(10), Token));
    }

    [Fact]
    public async Task ListReturnsTheLiveKeysUnderAPrefix()
    {
        var clock = new Clock();
        var store = new InMemoryClusterStore(() => clock.Now);
        await store.SetAsync("node:a", "1", TimeSpan.FromSeconds(5), Token);
        await store.SetAsync("node:b", "2", null, Token);
        await store.SetAsync("lease:x", "3", null, Token);
        clock.Now += TimeSpan.FromSeconds(6);

        var nodes = await store.ListAsync("node:", Token);

        Assert.Equal(new[] { "node:b" }, nodes.Keys.ToArray());
    }

    // ---- lease ----

    [Fact]
    public async Task OnlyOneNodeHoldsALeaseAndTheOtherGetsItOnceItIsReleased()
    {
        var store = new InMemoryClusterStore();
        var one = new ClusterJobLease(Node("one", store), NullLogger<ClusterJobLease>.Instance);
        var two = new ClusterJobLease(Node("two", store), NullLogger<ClusterJobLease>.Instance);

        var held = await one.TryAcquireAsync("replication:7", Token);
        Assert.NotNull(held);
        Assert.Null(await two.TryAcquireAsync("replication:7", Token));
        Assert.Null(await one.TryAcquireAsync("replication:7", Token));

        // Another job is independent.
        await using (var other = await two.TryAcquireAsync("replication:8", Token)) Assert.NotNull(other);

        await held!.DisposeAsync();

        await using var second = await two.TryAcquireAsync("replication:7", Token);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task ALeaseIsRenewedWhileHeldAndFreedWhenItsHolderStopsRenewing()
    {
        var store = new InMemoryClusterStore();
        var duration = TimeSpan.FromMilliseconds(600);
        var one = new ClusterJobLease(Node("one", store, duration), NullLogger<ClusterJobLease>.Instance);
        var two = new ClusterJobLease(Node("two", store, duration), NullLogger<ClusterJobLease>.Instance);

        await using (var held = await one.TryAcquireAsync("job", Token))
        {
            Assert.NotNull(held);

            // Held for several lease durations: the renewals keep it.
            await Task.Delay(TimeSpan.FromMilliseconds(1800), Token);
            Assert.Null(await two.TryAcquireAsync("job", Token));
        }

        // A holder that died is a lease nobody renews: it expires by itself.
        Assert.True(await store.SetIfAbsentAsync("lease:dead", "ghost|token|2026-01-01T00:00:00.0000000Z", TimeSpan.FromMilliseconds(300), Token));
        Assert.Null(await two.TryAcquireAsync("dead", Token));
        await Task.Delay(TimeSpan.FromMilliseconds(500), Token);

        await using var taken = await two.TryAcquireAsync("dead", Token);
        Assert.NotNull(taken);
    }

    [Fact]
    public async Task ALeaseIsRefusedWhenTheStoreCannotBeReachedSoTheJobIsSkippedNotRunTwice()
    {
        var store = new InMemoryClusterStore { Unavailable = true };
        var lease = new ClusterJobLease(Node("one", store), NullLogger<ClusterJobLease>.Instance);

        Assert.Null(await lease.TryAcquireAsync("job", Token));
    }

    [Fact]
    public async Task AWorkerBuiltWithoutALeaseServiceRunsEverything()
    {
        await using var handle = await ((IJobLease?)null).AcquireAsync("anything", Token);

        Assert.NotNull(handle);
    }

    // ---- rules and options ----

    [Theory]
    [InlineData("Sqlite", "AwsS3", "SQLite")]
    [InlineData("SqlServer", "Filesystem", "file system")]
    [InlineData("AzureTable", "AwsS3", "Azure Table")]
    [InlineData(null, "AwsS3", "SQLite")]
    [InlineData("PostgreSql", null, "file system")]
    public void ClusterModeRefusesALocalDatabaseOrStorage(string? database, string? storage, string mentions)
    {
        var problems = new HaBackendRules().Check(
            new HaOptions { Enabled = true, ConnectionString = "redis:6379" },
            new DatabaseOptions { Type = database },
            new StorageOptions { Type = storage });

        Assert.Contains(problems, p => p.Contains(mentions, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ASharedDatabaseAndStorageWithARedisStringAreAccepted()
    {
        foreach (var database in new[] { "SqlServer", "PostgreSql", "MySql" })
        {
            Assert.Empty(new HaBackendRules().Check(
                new HaOptions { Enabled = true, ConnectionString = "redis:6379" },
                new DatabaseOptions { Type = database },
                new StorageOptions { Type = "AzureBlobStorage" }));
        }
    }

    [Fact]
    public void ClusterModeNeedsTheRedisConnectionString()
    {
        var problems = new HaBackendRules().Check(
            new HaOptions { Enabled = true },
            new DatabaseOptions { Type = "PostgreSql" },
            new StorageOptions { Type = "AwsS3" });

        Assert.Single(problems);
        Assert.Contains("Ha:ConnectionString", problems[0]);
    }

    [Fact]
    public void WithoutHaNothingIsCheckedSoASingleNodeKeepsSqliteAndTheFileSystem()
    {
        Assert.Empty(new HaBackendRules().Check(new HaOptions(), new DatabaseOptions { Type = "Sqlite" }, new StorageOptions { Type = "Filesystem" }));
    }

    [Fact]
    public void OptionsAreValidatedOnlyWhenEnabledAndNeedASaneLeaseAndInterval()
    {
        Assert.Empty(new HaOptions { LeaseDuration = TimeSpan.Zero }.Validate());

        var failures = new HaOptions { Enabled = true, LeaseDuration = TimeSpan.FromSeconds(1), SyncInterval = TimeSpan.Zero, KeyPrefix = " ", Backend = "Memcached" }.Validate();

        Assert.Equal(4, failures.Count);
    }

    [Fact]
    public void WithoutHaTheRuntimeHasNoStoreEvenIfOneIsRegistered()
    {
        var runtime = new ClusterRuntime(Options.Create(new HaOptions { Enabled = false }), new InMemoryClusterStore());

        Assert.False(runtime.IsEnabled);
        Assert.Null(runtime.Store);
    }
}
