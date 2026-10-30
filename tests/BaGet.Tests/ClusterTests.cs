using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.RateLimiting;

namespace BaGet.Tests;

/// <summary>
/// High availability (P10): two real nodes sharing one database, one package folder and one cluster store. Jobs run once, the
/// read-only gate and the access model are shared, the data protection keys are in the database, the rate limits count together,
/// and a node that cannot run in a cluster refuses to start. The health endpoints and the Cluster page report it.
/// </summary>
public class ClusterTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<IDisposable> _disposables = new();

    public ClusterTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ClusterHarness Cluster(IClusterStore? store = null, Action<IServiceCollection>? configure = null, Dictionary<string, string>? extra = null)
    {
        var harness = new ClusterHarness(_output, store, configure, extra);
        _disposables.Add(harness);

        return harness;
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var value = await read();
            if (done(value) || DateTime.UtcNow > deadline) return value;
            await Task.Delay(50, Token);
        }
    }

    // ---- startup ----

    [Fact]
    public void ANodeWithHaOnSqliteAndTheFileSystemRefusesToStart()
    {
        var logs = new CapturingLoggerProvider();
        var app = new BaGetApplication(
            _output,
            configOverrides: new Dictionary<string, string> { { "Ha:Enabled", "true" }, { "Ha:ConnectionString", "redis:6379" } },
            configureServices: services =>
            {
                services.AddSingleton<IClusterStore>(new InMemoryClusterStore());
                services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(logs);
            });
        _disposables.Add(app);

        // Main stops before the host starts, so the factory finds no server; the reasons are in the log.
        Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var text = string.Join(" | ", logs.Messages.Distinct());
        Assert.Contains("High availability", text);
        Assert.Contains("SQLite", text);
        Assert.Contains("file system", text);
    }

    private sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages;

            public CapturingLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) => _messages = messages;

            #pragma warning disable CS8633
            public IDisposable? BeginScope<TState>(TState state) => null;
            #pragma warning restore CS8633

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _messages.Enqueue(formatter(state, exception));
        }
    }

    [Fact]
    public async Task TheStartupGuardAloneAlsoRefusesWhenTheHostIsStartedWithoutMain()
    {
        var guard = new HaStartupGuard(
            Microsoft.Extensions.Options.Options.Create(new HaOptions { Enabled = true, ConnectionString = "redis:6379" }),
            Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { Type = "Sqlite" }),
            Microsoft.Extensions.Options.Options.Create(new StorageOptions { Type = "AwsS3" }),
            new HaBackendRules(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HaStartupGuard>.Instance);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.StartAsync(Token));

        Assert.Contains("SQLite", failure.Message);
    }

    private static IEnumerable<Exception> Chain(Exception? e)
    {
        for (; e != null; e = e.InnerException) yield return e;
    }

    [Fact]
    public void ASingleNodeNeedsNoRedisAndKeepsTheInProcessLease()
    {
        using var app = new BaGetApplication(_output);
        _ = app.Services;

        var runtime = app.Services.GetRequiredService<ClusterRuntime>();

        Assert.False(runtime.IsEnabled);
        Assert.Null(app.Services.GetService<IClusterStore>());
        Assert.IsType<InProcessJobLease>(app.Services.GetRequiredService<IJobLease>());
    }

    [Fact]
    public void AClusterNodeUsesTheSharedLeaseAndTheSharedStore()
    {
        var c = Cluster();

        Assert.IsType<ClusterJobLease>(c.One.Services.GetRequiredService<IJobLease>());
        Assert.Same(c.Store, c.One.Services.GetRequiredService<ClusterRuntime>().Store);
        Assert.Equal("node-one", c.One.Services.GetRequiredService<ClusterRuntime>().NodeId);
        Assert.Equal("node-two", c.Two.Services.GetRequiredService<ClusterRuntime>().NodeId);
    }

    // ---- health ----

    [Fact]
    public async Task LiveAnswersAndReadyChecksTheDatabaseAndStorageOfASingleNode()
    {
        using var app = new BaGetApplication(_output);
        using var client = app.CreateClient();

        using var live = await client.GetAsync("/health/live", Token);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync(Token));

        using var ready = await client.GetAsync("/health/ready", Token);
        var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(Token)).RootElement;
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("database").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("storage").GetString());
        Assert.False(body.GetProperty("checks").TryGetProperty("cluster", out _));
    }

    [Fact]
    public async Task ReadyReportsTheSharedStoreOfAClusterNodeButStaysReadyWithoutIt()
    {
        var c = Cluster();
        using var client = c.One.CreateClient();

        using (var healthy = await client.GetAsync("/health/ready", Token))
        {
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            Assert.Contains("\"cluster\":\"Healthy\"", await healthy.Content.ReadAsStringAsync(Token));
        }

        ((InMemoryClusterStore)c.Store).Unavailable = true;
        await c.One.Services.GetRequiredService<ReadinessService>().CheckAsync(force: true, Token);

        using var broken = await client.GetAsync("/health/ready", Token);
        var text = await broken.Content.ReadAsStringAsync(Token);
        Assert.Equal(HttpStatusCode.OK, broken.StatusCode);
        Assert.Contains("\"cluster\":\"Unhealthy\"", text);
        // The body names checks and results only.
        Assert.DoesNotContain("not-used-by-the-in-memory-store", text);
        Assert.DoesNotContain("unavailable", text, StringComparison.OrdinalIgnoreCase);
        ((InMemoryClusterStore)c.Store).Unavailable = false;
    }

    [Fact]
    public async Task ReadinessDoesNotDependOnTheReadOnlyGate()
    {
        using var app = new BaGetApplication(_output);
        app.Services.GetRequiredService<MaintenanceGate>().Close("copying");
        using var client = app.CreateClient();

        using var ready = await client.GetAsync("/health/ready", Token);

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    // ---- shared read-only gate ----

    [Fact]
    public async Task AGateClosedOnOneNodeClosesTheOtherAndReopensWithIt()
    {
        var c = Cluster();
        var gateOne = c.One.Services.GetRequiredService<MaintenanceGate>();
        var gateTwo = c.Two.Services.GetRequiredService<MaintenanceGate>();

        gateOne.Close("Copying the database");
        await c.SyncAsync();

        Assert.True(gateTwo.IsClosed);
        Assert.Equal("Copying the database", gateTwo.Reason);
        Assert.Throws<MaintenanceException>(() => gateTwo.ThrowIfClosed());

        // A write on the other node is refused with the service's 503.
        using var client = c.Two.CreateClient();
        using var push = new HttpRequestMessage(HttpMethod.Put, "/api/v2/package") { Content = new ByteArrayContent(Array.Empty<byte>()) };
        push.Headers.Add("X-NuGet-ApiKey", ClusterHarness.GlobalKey);
        using var refused = await client.SendAsync(push, Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);

        // Only the node that closed it reopens it.
        Assert.False(gateTwo.Open());
        Assert.True(gateTwo.IsClosed);

        Assert.True(gateOne.Open());
        await c.SyncAsync();
        Assert.False(gateTwo.IsClosed);
    }

    [Fact]
    public async Task AGateHeldByANodeThatStoppedRenewingIsReleasedByTheOthers()
    {
        var c = Cluster();
        var gateTwo = c.Two.Services.GetRequiredService<MaintenanceGate>();

        // A node that died mid-copy: its closure is in the store with the time to live it last renewed.
        await c.Store.SetAsync(ClusterRuntime.Keys.Gate, "ghost|Copying the database", TimeSpan.FromMilliseconds(700), Token);
        await SyncOnlyTwo(c);
        Assert.True(gateTwo.IsClosed);

        await Task.Delay(TimeSpan.FromMilliseconds(900), Token);
        await SyncOnlyTwo(c);

        Assert.False(gateTwo.IsClosed);
    }

    private static Task SyncOnlyTwo(ClusterHarness c) => ClusterHarness.SyncOf(c.Two).SyncAsync(Token);

    [Fact]
    public async Task AClosureUntilRestartStaysOnItsOwnNode()
    {
        var c = Cluster();
        var gateOne = c.One.Services.GetRequiredService<MaintenanceGate>();

        gateOne.CloseUntilRestart("The database was copied; restart BaGet to use the new database");
        await c.SyncAsync();

        Assert.False(c.Two.Services.GetRequiredService<MaintenanceGate>().IsClosed);
        Assert.Null(await c.Store.GetAsync(ClusterRuntime.Keys.Gate, Token));
    }

    // ---- shared access model and copy status ----

    [Fact]
    public async Task AnAccessModelChangeOnOneNodeMakesTheOtherReloadIt()
    {
        var c = Cluster();
        await c.SyncAsync();
        var versionTwo = c.Two.Services.GetRequiredService<AccessModelVersion>();
        var before = versionTwo.Current;

        using (var scope = c.One.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccessAdministration>().CreateGroupAsync("builders", "Build agents", "root", Token);
        }

        await c.SyncAsync();

        Assert.True(versionTwo.Current > before, "The second node did not learn of the change.");
    }

    [Fact]
    public async Task ACopyRunningOnOneNodeIsShownOnTheOtherAndASecondIsRefused()
    {
        var c = Cluster();
        var running = DatabaseCopyStatus.Idle with { Phase = DatabaseCopyPhase.Copying, Table = "Packages", TablesTotal = 20, TablesDone = 3, Message = "Copying Packages" };
        await c.Store.SetAsync(ClusterRuntime.Keys.CopyStatus, JsonSerializer.Serialize(running, ClusterSyncService.Json), TimeSpan.FromMinutes(5), Token);

        await c.SyncAsync();

        var copy = c.Two.Services.GetRequiredService<DatabaseCopyService>();
        Assert.Equal(DatabaseCopyPhase.Copying, copy.Status.Phase);
        Assert.Equal(3, copy.Status.TablesDone);
        Assert.False(copy.TryQueue(new DatabaseCopyRequest("Sqlite", "Data Source=other.db", null, new Dictionary<SettingDescriptor, object>(), "root"), out var refusal));
        Assert.Equal(DatabaseCopyService.ClusterRefusal, refusal);
    }

    [Fact]
    public void ADatabaseCopyIsRefusedOnAClusterNodeAndNothingIsQueuedOrClosed()
    {
        var c = Cluster();
        var copy = c.One.Services.GetRequiredService<DatabaseCopyService>();

        Assert.False(copy.TryQueue(new DatabaseCopyRequest("Sqlite", "Data Source=other.db", null, new Dictionary<SettingDescriptor, object>(), "root"), out var refusal));

        Assert.Equal(DatabaseCopyService.ClusterRefusal, refusal);
        Assert.False(c.One.Services.GetRequiredService<MaintenanceGate>().IsClosed);
        Assert.Equal(DatabaseCopyPhase.Idle, copy.Status.Phase);
    }

    // ---- jobs run once ----

    private sealed class BlockingOperation : IAdminOperation
    {
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Runs;

        public string Name => "blocking-test";

        public string Title => "Blocking test";

        public string Description => "Waits until released.";

        public bool IsAvailable(IServiceProvider services) => true;

        public async Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            await Release.Task.WaitAsync(cancellationToken);

            return "done";
        }
    }

    [Fact]
    public async Task AnAdminOperationRunsOnOneNodeOfTheClusterAtATime()
    {
        var operation = new BlockingOperation();
        var c = Cluster(configure: services => services.AddSingleton<IAdminOperation>(operation));
        var repository = RepositoryInfo.Default;
        var jobsOne = c.One.Services.GetRequiredService<AdminJobService>();
        var jobsTwo = c.Two.Services.GetRequiredService<AdminJobService>();

        Assert.True(jobsOne.TryStart("blocking-test", repository, "root", out _));

        // The same operation and repository on the other node is refused: the lease is taken.
        Assert.False(jobsTwo.TryStart("blocking-test", repository, "root", out var refusal));
        Assert.Contains("another node", refusal);

        var leases = await c.Store.ListAsync(ClusterRuntime.Keys.LeasePrefix, Token);
        Assert.Contains(leases, l => l.Key.StartsWith("lease:operation:blocking-test", StringComparison.Ordinal) && l.Value.StartsWith("node-one|", StringComparison.Ordinal));

        operation.Release.SetResult(true);
        await jobsOne.WaitAsync("blocking-test", repository.Name);

        // Released: the other node may run it now.
        var again = await EventuallyAsync(
            () => Task.FromResult(jobsTwo.TryStart("blocking-test", repository, "root", out _)),
            started => started);
        Assert.True(again);
        await jobsTwo.WaitAsync("blocking-test", repository.Name);
        Assert.Equal(2, operation.Runs);
    }

    [Fact]
    public async Task ARetentionPolicyAndTheSchedulingPassRunOnOneNodeOfTheClusterAtATime()
    {
        var c = Cluster();
        var workerOne = c.One.Services.GetServices<IHostedService>().OfType<RetentionWorker>().Single();
        var workerTwo = c.Two.Services.GetServices<IHostedService>().OfType<RetentionWorker>().Single();
        var lease = c.One.Services.GetRequiredService<IJobLease>();

        // Node one holds the scheduling pass: node two's pass starts nothing, even with the policy due.
        await using (var pass = await lease.TryAcquireAsync("retention:schedule", Token))
        {
            Assert.NotNull(pass);
            Assert.Empty(await workerTwo.RunDueAsync(Token));
        }

        // And a manual run of a policy cannot start on node two while node one holds that policy's lease.
        await using (var policy = await lease.TryAcquireAsync("retention:policy:7", Token))
        {
            Assert.NotNull(policy);
            Assert.False(c.Two.Services.GetRequiredService<RetentionJobs>().TryStart(7, dryRun: false, "root", out var refusal));
            Assert.Contains("another node", refusal);
        }

        Assert.Empty(await workerOne.RunDueAsync(Token));
    }

    // ---- data protection ----

    [Fact]
    public async Task TheDataProtectionKeysAreInTheDatabaseSoANodeReadsWhatTheOtherProtected()
    {
        var c = Cluster();

        var protectedText = c.One.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("secret");
        var read = c.Two.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedText);

        Assert.Equal("secret", read);
        using var scope = c.One.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IContext>().DataProtectionKeys!.AnyAsync(Token));
    }

    [Fact]
    public async Task ANewDataProtectionKeyIsStoredWhileTheReadOnlyGateIsClosed()
    {
        var c = Cluster();
        using var scope = c.One.Services.CreateScope();
        var before = await scope.ServiceProvider.GetRequiredService<IContext>().DataProtectionKeys!.CountAsync(Token);
        c.One.Services.GetRequiredService<MaintenanceGate>().Close("copying");

        // Without the exemption this throws MaintenanceException: sign-in and antiforgery could not get a key while the cluster is read-only.
        c.One.Services.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30));

        Assert.Equal(before + 1, await scope.ServiceProvider.GetRequiredService<IContext>().DataProtectionKeys!.CountAsync(Token));
        // The gate still refuses the application's own writes.
        Assert.True(c.One.Services.GetRequiredService<MaintenanceGate>().IsClosed);
    }

    [Fact]
    public async Task ASessionCookieIssuedByOneNodeSignsTheUserInOnTheOther()
    {
        var c = Cluster();
        using var browserOne = new Browser(c.One);
        using var login = await browserOne.PostAsync("account/login", new Dictionary<string, string> { { "UserName", "root" }, { "Password", Password } });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        // The cookies the first node issued, replayed to the second.
        var cookies = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/cluster");
        request.Headers.Add("Cookie", cookies);
        using var client = c.Two.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- rate limits ----

    [Fact]
    public void TheContactAllowanceIsSharedByTheNodes()
    {
        var c = Cluster();
        var one = c.One.Services.GetRequiredService<ContactRateLimiter>();
        var two = c.Two.Services.GetRequiredService<ContactRateLimiter>();

        Assert.True(one.TryAcquire("alice", "Contoso.Lib"));
        Assert.False(two.TryAcquire("alice", "contoso.lib"));
        Assert.True(two.TryAcquire("bob", "Contoso.Lib"));

        one.Release("alice", "Contoso.Lib");
        Assert.True(two.TryAcquire("alice", "Contoso.Lib"));
    }

    [Fact]
    public void ThePasswordResetLimitsCountAcrossNodes()
    {
        var c = Cluster();
        var one = c.One.Services.GetRequiredService<PasswordResetRateLimiter>();
        var two = c.Two.Services.GetRequiredService<PasswordResetRateLimiter>();

        Assert.True(one.TryAcquire("10.0.0.1", "alice"));
        Assert.False(two.TryAcquire("10.0.0.2", "alice"));

        // One client over its cap is refused on whichever node it reaches, and the refusal gives the account its allowance back.
        for (var i = 0; i < PasswordResetRateLimiter.MaxPerClient; i++)
        {
            Assert.True((i % 2 == 0 ? one : two).TryAcquire("10.0.0.9", "user" + i));
        }

        Assert.False(two.TryAcquire("10.0.0.9", "last"));
        Assert.True(one.TryAcquire("10.0.0.3", "last"));
    }

    // ---- scanning in flight ----

    [Fact]
    public async Task AScanStartedOnOneNodeIsSeenByTheOther()
    {
        var c = Cluster();
        var store = c.Two.Services.GetRequiredService<ClusterRuntime>().Store;

        using (ScanInFlight.Begin(1, "app", "1.0", c.One.Services.GetRequiredService<ClusterRuntime>().Store))
        {
            Assert.True(await ScanInFlight.ContainsAsync(1, "other-name", "1.0", store, Token) == false);
            // A different process-local state: forget the local mark by asking for a version only the store knows.
            Assert.NotNull(await c.Store.GetAsync("scan-inflight:1:app:1.0", Token));
        }

        Assert.Null(await c.Store.GetAsync("scan-inflight:1:app:1.0", Token));
    }

    // ---- the Cluster page ----

    [Fact]
    public async Task TheClusterPageListsTheNodesTheLeasesAndTheHealthChecks()
    {
        var operation = new BlockingOperation();
        var c = Cluster(configure: services => services.AddSingleton<IAdminOperation>(operation));
        await c.SyncAsync();
        c.One.Services.GetRequiredService<AdminJobService>().TryStart("blocking-test", RepositoryInfo.Default, "root", out _);
        using var browser = new Browser(c.Two);
        await SignInAsync(browser, "root");

        var html = await browser.GetPageAsync("/admin/cluster");

        Assert.Contains("data-node=\"node-one\"", html);
        Assert.Contains("data-node=\"node-two\"", html);
        Assert.Contains("(this node)", html);
        Assert.Contains("data-job=\"operation:blocking-test:default\"", html);
        Assert.Contains("data-check=\"database\"", html);
        Assert.Contains("data-check=\"storage\"", html);
        Assert.Contains("data-check=\"cluster\"", html);
        Assert.DoesNotContain("not-used-by-the-in-memory-store", html);
        operation.Release.SetResult(true);
    }

    [Fact]
    public async Task TheClusterPageOfASingleNodeSaysClusteringIsOffAndIsForAdministratorsOnly()
    {
        using var app = new BaGetApplication(_output, configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } });
        await CreateUserAsync(app, "root", administrator: true);
        await CreateUserAsync(app, "alice");

        using var admin = new Browser(app);
        await SignInAsync(admin, "root");
        var html = await admin.GetPageAsync("/admin/cluster");
        Assert.Contains("id=\"cluster-off\"", html);
        Assert.Contains("data-check=\"database\"", html);

        using var alice = new Browser(app);
        await SignInAsync(alice, "alice");
        using var denied = await alice.GetAsync("/admin/cluster");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task ANodeThatStopsLeavesTheListing()
    {
        var c = Cluster();
        await c.SyncAsync();
        Assert.NotNull(await c.Store.GetAsync(ClusterRuntime.Keys.Node("node-two"), Token));

        await ClusterHarness.SyncOf(c.Two).StopAsync(Token);

        Assert.Null(await c.Store.GetAsync(ClusterRuntime.Keys.Node("node-two"), Token));
        Assert.NotNull(await c.Store.GetAsync(ClusterRuntime.Keys.Node("node-one"), Token));
    }

    // ---- settings ----

    [Fact]
    public void TheHaSettingsAreOnTheSettingsPageWithTheConnectionStringSensitiveAndEverythingRestartRequired()
    {
        using var app = new BaGetApplication(_output);
        var catalog = app.Services.GetRequiredService<SettingsCatalog>();
        var ha = catalog.InSection("Ha").ToList();

        Assert.Contains(ha, d => d.Key == "Ha:Enabled");
        Assert.All(ha, d => Assert.True(d.RequiresRestart, d.Key));
        Assert.True(ha.Single(d => d.Key == "Ha:ConnectionString").IsSensitive);
    }
}
