using System.Text.Json.Serialization;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>
/// A replication as the API takes it. The peer API key is write only: <see cref="ApiKey"/> empty keeps the stored key on an update (changing
/// the peer address drops it), <see cref="ClearApiKey"/> forgets it; no response ever carries it.
/// </summary>
public sealed class ReplicationRequest
{
    /// <summary>The unique name.</summary>
    public string? Name { get; set; }

    /// <summary>The local repository.</summary>
    public string? Repository { get; set; }

    /// <summary><c>Push</c> (this repository to the peer) or <c>Pull</c>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReplicationDirection Direction { get; set; }

    /// <summary>The peer server's base address.</summary>
    public string? PeerUrl { get; set; }

    /// <summary>The peer's repository (<c>default</c> or its name).</summary>
    public string? PeerRepository { get; set; }

    /// <summary>The peer's API key (an administrator's key with <c>admin:api</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Forget the stored key.</summary>
    public bool ClearApiKey { get; set; }

    /// <summary>A disabled replication never runs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Push only: run after a deploy to the local repository.</summary>
    public bool OnDeploy { get; set; }

    /// <summary>Run every N minutes; none = only on request.</summary>
    public int? ScheduleMinutes { get; set; }

    /// <summary>Delete at the target what was copied and is deleted at the source.</summary>
    public bool PropagateDeletes { get; set; }

    /// <summary><c>TargetWins</c> or <c>SourceWins</c>.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReplicationConflict Conflict { get; set; }

    internal ReplicationInput ToInput(int key)
        => new(key, Name, Repository, Direction, PeerUrl, PeerRepository, ApiKey, ClearApiKey, Enabled, OnDeploy, ScheduleMinutes, PropagateDeletes, Conflict);
}
