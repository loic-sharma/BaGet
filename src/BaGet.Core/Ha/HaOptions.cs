using System;
using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>
/// High availability (<c>Ha:*</c>): several BaGet nodes behind a load balancer sharing one database, one object store and one
/// Redis. Off by default; a single node then behaves as always and needs no Redis. Everything here is read when the host starts
/// (registration decides from <c>Ha:Enabled</c>), so every setting needs a restart. See <c>docs/high-availability.md</c>.
/// </summary>
[RestartRequired]
public class HaOptions
{
    /// <summary>
    /// Turns cluster mode on. The node then refuses to start unless the database is shared (not SQLite or Azure Table), the storage is
    /// shared (not the file system) and <see cref="ConnectionString"/> is set.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>The shared backend: only <c>Redis</c> today.</summary>
    [AllowedSettingValues("Redis")]
    public string Backend { get; set; } = "Redis";

    /// <summary>The Redis connection string (<c>host:6379,password=...,ssl=true</c>). Never shown or logged.</summary>
    [SensitiveSetting]
    [SettingExample(nameof(Backend), "Redis", "redis.example.com:6379,password=<password>,ssl=true")]
    public string? ConnectionString { get; set; }

    /// <summary>The name this node shows on the cluster page. Default: the machine name plus a random suffix, new at every start.</summary>
    public string? NodeName { get; set; }

    /// <summary>A prefix for every key BaGet writes, so several clusters can share one Redis. Default <c>baget</c>.</summary>
    public string KeyPrefix { get; set; } = "baget";

    /// <summary>
    /// Path of a PFX certificate (with private key) that encrypts the data protection key ring stored in the database. Every node needs
    /// the same certificate. Without it the keys that protect remote-repository passwords and replication peer keys are plain XML in the
    /// same database as those secrets, so set it (the node logs a warning at start otherwise).
    /// </summary>
    public string? KeyCertificatePath { get; set; }

    /// <summary>The password of the PFX at <see cref="KeyCertificatePath"/>, if it has one.</summary>
    [SensitiveSetting]
    public string? KeyCertificatePassword { get; set; }

    /// <summary>
    /// How long a job lease or the read-only gate of a node that stopped answering stays held. The holder renews it every third of
    /// this, so a crashed node frees its jobs within this time. Default 30 seconds, at least 3.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often a node publishes its heartbeat and picks up what the others changed (read-only gate, access model, database copy
    /// status). Default 2 seconds, at least 100 ms.
    /// </summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>What is wrong with these options, one message per problem (empty when valid or when HA is off).</summary>
    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();
        if (!Enabled) return failures;

        if (!string.Equals(Backend, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("The 'Ha:Backend' config is invalid. Allowed values: Redis");
        }

        if (LeaseDuration < TimeSpan.FromSeconds(3))
        {
            failures.Add("The 'Ha:LeaseDuration' config must be at least 3 seconds.");
        }

        if (SyncInterval < TimeSpan.FromMilliseconds(100))
        {
            failures.Add("The 'Ha:SyncInterval' config must be at least 100 milliseconds.");
        }

        if (string.IsNullOrWhiteSpace(KeyPrefix))
        {
            failures.Add("The 'Ha:KeyPrefix' config must not be empty.");
        }

        return failures;
    }
}
