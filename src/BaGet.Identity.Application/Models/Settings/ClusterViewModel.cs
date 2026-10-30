using BaGet.Core;

namespace BaGet.Identity.Application.Models.Settings;

/// <summary>What the cluster screen (<c>/admin/cluster</c>) shows: nodes, leases, shared state and health.</summary>
public sealed class ClusterViewModel
{
    /// <summary>The status read for the screen.</summary>
    public ClusterStatus Status { get; internal set; } = null!;
}
