using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Two real BaGet servers in one process: <see cref="Local"/> (the one that replicates) and <see cref="Peer"/> (another server it
/// reaches through its HTTP pipeline, the way a second <c>BaGetServer</c> is reached by the client rigs). The local server's
/// <see cref="IReplicationPeerFactory"/> hands out clients of the peer, authenticated with the peer's key as bearer, exactly what the
/// real factory does over the network. <see cref="FailPeer"/> makes every request fail like an unreachable server.
/// </summary>
internal sealed class ReplicationHarness : IDisposable
{
    public const string PeerAddress = "http://peer.test/";

    private volatile bool _down;

    public ReplicationHarness(ITestOutputHelper output, Dictionary<string, string>? localSettings = null, Action<IServiceCollection>? configureLocal = null)
    {
        Peer = new AdminApiHarness(output);
        PeerKey = Peer.KeyAsync("root", ApiKeyScopes.All.Append(ApiKeyScopes.Admin).ToArray()).GetAwaiter().GetResult();
        Local = new AdminApiHarness(output, localSettings, services =>
        {
            services.AddSingleton<IReplicationPeerFactory>(new PeerFactory(this));
            configureLocal?.Invoke(services);
        });
    }

    public AdminApiHarness Local { get; }

    public AdminApiHarness Peer { get; }

    /// <summary>The peer's administrator key with every scope and <c>admin:api</c>.</summary>
    public string PeerKey { get; }

    /// <summary>The key requests to the peer carry, for a test that wants a wrong one.</summary>
    public string? KeyOverride { get; set; }

    /// <summary>While true every request to the peer fails as if it could not be reached.</summary>
    public bool FailPeer
    {
        get => _down;
        set => _down = value;
    }

    /// <summary>Requests the peer received (method and path), in order.</summary>
    public List<string> PeerRequests { get; } = new();

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Local.Dispose();
        Peer.Dispose();
    }

    /// <summary>Creates (or saves) a replication on the local server.</summary>
    public async Task<int> CreateAsync(
        string name = "r1",
        string repository = "default",
        string peerRepository = "default",
        ReplicationDirection direction = ReplicationDirection.Push,
        bool propagateDeletes = false,
        ReplicationConflict conflict = ReplicationConflict.TargetWins,
        bool onDeploy = false,
        int? scheduleMinutes = null,
        bool enabled = true,
        string? apiKey = null)
    {
        var config = await Local.WithAsync(async services => await services.GetRequiredService<IReplicationAdministration>().SaveAsync(
            new ReplicationInput(0, name, repository, direction, PeerAddress, peerRepository, apiKey ?? PeerKey, false, enabled, onDeploy, scheduleMinutes, propagateDeletes, conflict),
            "root",
            Token));

        return config.Key;
    }

    /// <summary>Runs the replication to its end on the local server and returns the log entry.</summary>
    public Task<ReplicationRun> RunAsync(int key, string trigger = "manual", int? resumedFrom = null, Func<int, CancellationToken, Task>? afterItem = null, CancellationTokenSource? stop = null)
        => Local.WithAsync(async services =>
        {
            var engine = services.GetRequiredService<ReplicationEngine>();
            engine.AfterItem = afterItem;

            return await engine.RunAsync(key, trigger, "root", resumedFrom, stop?.Token ?? Token);
        });

    public Task<ReplicationInventory> InventoryAsync(AdminApiHarness side, string repository = "default")
        => side.WithAsync(async services => (await services.GetRequiredService<IReplicationInventory>().GetAsync(repository, false, Token))!);

    public async Task<IReadOnlyList<ReplicationRun>> RunsAsync(int key)
        => await Local.WithAsync(services => services.GetRequiredService<IReplicationAdministration>().ListRunsAsync(key, 50, Token));

    private sealed class PeerFactory : IReplicationPeerFactory
    {
        private readonly ReplicationHarness _owner;

        public PeerFactory(ReplicationHarness owner)
        {
            _owner = owner;
        }

        public ReplicationPeer Create(string peerUrl, string peerRepository, string? apiKey, int timeoutSeconds = 600)
        {
            var client = new HttpClient(new Gate(_owner, _owner.Peer.App.Server.CreateHandler())) { BaseAddress = new Uri(peerUrl) };
            if (!string.IsNullOrEmpty(apiKey)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _owner.KeyOverride ?? apiKey);

            return new ReplicationPeer(client, new Uri(peerUrl), peerRepository, ownsClient: true, apiKey: string.IsNullOrEmpty(apiKey) ? null : _owner.KeyOverride ?? apiKey);
        }
    }

    private sealed class Gate : DelegatingHandler
    {
        private readonly ReplicationHarness _owner;

        public Gate(ReplicationHarness owner, HttpMessageHandler inner)
            : base(inner)
        {
            _owner = owner;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_owner._down) throw new HttpRequestException("The peer could not be reached (test).");

            lock (_owner.PeerRequests) _owner.PeerRequests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");

            return base.SendAsync(request, cancellationToken);
        }
    }
}
