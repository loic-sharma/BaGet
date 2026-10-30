using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>
/// What the replication form posts (<c>Form.*</c>): a <see cref="ReplicationConfig"/> as a person types it. The peer's API key is write
/// only: it is never filled back into a page; an empty field keeps the stored key.
/// </summary>
public sealed class ReplicationForm
{
    public string? Name { get; set; }

    public string? Repository { get; set; }

    /// <summary><c>Push</c> or <c>Pull</c>.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ReplicationDirection Direction { get; set; } = ReplicationDirection.Push;

    public string? PeerUrl { get; set; }

    public string? PeerRepository { get; set; } = BaGet.Core.Repository.DefaultName;

    /// <summary>The peer's API key (write only).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Forget the stored key.</summary>
    public bool ClearApiKey { get; set; }

    public bool Enabled { get; set; } = true;

    public bool OnDeploy { get; set; }

    public int? ScheduleMinutes { get; set; }

    public bool PropagateDeletes { get; set; }

    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ReplicationConflict Conflict { get; set; } = ReplicationConflict.TargetWins;

    /// <summary>A copy without the peer's API key: what a screen's model carries back, so the key typed in a request is never echoed (JSON included).</summary>
    public ReplicationForm WithoutSecret() => new()
    {
        Name = Name,
        Repository = Repository,
        Direction = Direction,
        PeerUrl = PeerUrl,
        PeerRepository = PeerRepository,
        ApiKey = null,
        ClearApiKey = ClearApiKey,
        Enabled = Enabled,
        OnDeploy = OnDeploy,
        ScheduleMinutes = ScheduleMinutes,
        PropagateDeletes = PropagateDeletes,
        Conflict = Conflict,
    };

    /// <summary>The administration's input for configuration <paramref name="key"/> (0 creates).</summary>
    public ReplicationInput ToInput(int key)
        => new(key, Name, Repository, Direction, PeerUrl, PeerRepository, ApiKey, ClearApiKey, Enabled, OnDeploy, ScheduleMinutes, PropagateDeletes, Conflict);

    /// <summary>The form filled in from a stored configuration (without its key).</summary>
    public static ReplicationForm From(ReplicationConfigView view)
    {
        var c = view.Config;

        return new ReplicationForm
        {
            Name = c.Name,
            Repository = view.Repository,
            Direction = c.Direction,
            PeerUrl = c.PeerUrl,
            PeerRepository = c.PeerRepository,
            Enabled = c.Enabled,
            OnDeploy = c.OnDeploy,
            ScheduleMinutes = c.ScheduleMinutes,
            PropagateDeletes = c.PropagateDeletes,
            Conflict = c.Conflict,
        };
    }
}
