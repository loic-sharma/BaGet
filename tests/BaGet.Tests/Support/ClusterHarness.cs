using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Two real BaGet nodes in one process sharing one SQLite file, one package folder and one <see cref="IClusterStore"/> (the in-memory
/// one by default; a Redis store in <c>BaGet.Ha.Tests</c>), the way two nodes behind a balancer share SQL, an object store and Redis.
/// Cluster mode normally refuses SQLite and the file system; <see cref="HaBackendRules.AllowLocalBackends"/> lifts that for the test.
/// Both nodes have accounts on, the same global key and an administrator <c>root</c> (created once, in the shared database).
/// </summary>
internal sealed class ClusterHarness : IDisposable
{
    public const string GlobalKey = "global-key";

    private readonly string _directory;

    public ClusterHarness(
        ITestOutputHelper output,
        IClusterStore? store = null,
        Action<IServiceCollection>? configure = null,
        Dictionary<string, string>? extra = null,
        TimeSpan? leaseDuration = null)
    {
        _directory = Path.Combine(Path.GetTempPath(), "BaGetTests", "cluster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Store = store ?? new InMemoryClusterStore();

        One = new BaGetApplication(output, configOverrides: Settings("node-one", leaseDuration, extra), configureServices: Services(configure));
        // The first node creates and migrates the database before the second opens it.
        _ = One.Services;
        AdminTestSupport.CreateUserAsync(One, "root", administrator: true).GetAwaiter().GetResult();
        Two = new BaGetApplication(output, configOverrides: Settings("node-two", leaseDuration, extra), configureServices: Services(configure));
        _ = Two.Services;
    }

    public IClusterStore Store { get; }

    public BaGetApplication One { get; }

    public BaGetApplication Two { get; }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The node's sync service (it also runs on its own every <c>Ha:SyncInterval</c>).</summary>
    public static ClusterSyncService SyncOf(BaGetApplication node) => node.Services.GetServices<IHostedService>().OfType<ClusterSyncService>().Single();

    /// <summary>Runs one sync round on both nodes, one after the other.</summary>
    public async Task SyncAsync()
    {
        await SyncOf(One).SyncAsync(Token);
        await SyncOf(Two).SyncAsync(Token);
    }

    public void Dispose()
    {
        One.Dispose();
        Two.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private Dictionary<string, string> Settings(string name, TimeSpan? leaseDuration, Dictionary<string, string>? extra)
    {
        var settings = new Dictionary<string, string>
        {
            { "Database:ConnectionString", $"Data Source={Path.Combine(_directory, "BaGet.db")}" },
            { "Storage:Path", Path.Combine(_directory, "Packages") },
            { "Accounts:Enabled", "true" },
            { "ApiKey", GlobalKey },
            { "Ha:Enabled", "true" },
            { "Ha:ConnectionString", "not-used-by-the-in-memory-store" },
            { "Ha:NodeName", name },
            { "Ha:SyncInterval", "00:00:00.200" },
            { "Ha:LeaseDuration", (leaseDuration ?? TimeSpan.FromSeconds(10)).ToString("c") },
        };
        if (extra != null)
        {
            foreach (var pair in extra) settings[pair.Key] = pair.Value;
        }

        return settings;
    }

    private Action<IServiceCollection> Services(Action<IServiceCollection>? configure) => services =>
    {
        services.AddSingleton(new HaBackendRules { AllowLocalBackends = true });
        services.AddSingleton(Store);
        configure?.Invoke(services);
    };
}
