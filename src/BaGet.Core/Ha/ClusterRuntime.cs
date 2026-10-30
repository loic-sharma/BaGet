using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// What a node knows about the cluster it is in: whether it is in one, its name, and the shared store. Always registered; without
/// <c>Ha:Enabled</c> <see cref="IsEnabled"/> is false and <see cref="Store"/> is null, and every component keeps its in-process behaviour.
/// </summary>
public sealed class ClusterRuntime
{
    private readonly HaOptions _options;

    /// <summary>Creates the runtime.</summary>
    /// <param name="options">The HA options (read once; they need a restart).</param>
    /// <param name="store">The shared store, registered only in HA mode.</param>
    public ClusterRuntime(IOptions<HaOptions> options, IClusterStore? store = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value ?? new HaOptions();
        Store = _options.Enabled ? store : null;
        Started = DateTime.UtcNow;
        NodeId = string.IsNullOrWhiteSpace(_options.NodeName)
            ? Environment.MachineName.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)
            : _options.NodeName!.Trim();
    }

    /// <summary>Whether this node is part of a cluster (HA enabled and a store present).</summary>
    public bool IsEnabled => Store != null;

    /// <summary>The shared store, or null on a single node.</summary>
    public IClusterStore? Store { get; }

    /// <summary>This node's name on the cluster page and in lease holders.</summary>
    public string NodeId { get; }

    /// <summary>When this node started (UTC).</summary>
    public DateTime Started { get; }

    /// <summary>How long a lease lasts without being renewed.</summary>
    public TimeSpan LeaseDuration => _options.LeaseDuration;

    /// <summary>How often the node publishes and polls.</summary>
    public TimeSpan SyncInterval => _options.SyncInterval;

    /// <summary>How long a node's heartbeat stays listed after its last one.</summary>
    public TimeSpan HeartbeatTtl => TimeSpan.FromTicks(Math.Max(SyncInterval.Ticks * 4, TimeSpan.FromSeconds(10).Ticks));

    /// <summary>Key names (relative to <c>Ha:KeyPrefix</c>, which the store adds).</summary>
    public static class Keys
    {
        public const string NodePrefix = "node:";
        public const string LeasePrefix = "lease:";
        public const string Gate = "gate";
        public const string AccessVersion = "access-version";
        public const string CopyStatus = "copy-status";

        public static string Node(string id) => NodePrefix + id;

        public static string Lease(string job) => LeasePrefix + job;
    }
}

/// <summary>
/// The storage and database a cluster may use: both must be reachable from every node. Checked at startup (the node refuses to start)
/// and by <c>ValidateStartupOptions</c>.
/// </summary>
public sealed class HaBackendRules
{
    /// <summary>Tests and a single-host trial may accept a local database and the file system; production never should.</summary>
    public bool AllowLocalBackends { get; init; }

    /// <summary>What stops <paramref name="ha"/> from running on this database and storage (empty when nothing).</summary>
    public IReadOnlyList<string> Check(HaOptions ha, DatabaseOptions? database, StorageOptions? storage)
    {
        if (ha == null) throw new ArgumentNullException(nameof(ha));
        if (!ha.Enabled) return Array.Empty<string>();

        var problems = ha.Validate().ToList();
        if (string.IsNullOrWhiteSpace(ha.ConnectionString))
        {
            problems.Add("Ha:ConnectionString is required (the Redis connection string).");
        }

        if (AllowLocalBackends) return problems;

        var databaseType = database?.Type;
        if (string.IsNullOrWhiteSpace(databaseType) || string.Equals(databaseType, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Ha:Enabled needs a database every node can reach: SQLite is a file on one machine (use SqlServer, PostgreSql or MySql).");
        }
        else if (string.Equals(databaseType, "AzureTable", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Ha:Enabled keeps the data protection keys in a relational database: Azure Table is not supported (use SqlServer, PostgreSql or MySql).");
        }

        var storageType = storage?.Type;
        if (string.IsNullOrWhiteSpace(storageType) || string.Equals(storageType, "Filesystem", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Ha:Enabled needs storage every node can reach: the file system is local to one machine (use AwsS3, AzureBlobStorage, GoogleCloud or AliyunOss).");
        }

        return problems;
    }
}
