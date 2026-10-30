using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the replications screen (<c>/admin/replication</c>) shows: every replication and the form that creates one.</summary>
public sealed class ReplicationViewModel : PolicyViewModel
{
    /// <summary>The new replication.</summary>
    public ReplicationForm Form { get; set; } = new();

    /// <summary>Every replication.</summary>
    public IReadOnlyList<ReplicationConfigView> Replications { get; internal set; } = Array.Empty<ReplicationConfigView>();

    /// <summary>The local repositories a replication can name.</summary>
    public IReadOnlyList<RepositoryInfo> Repositories { get; internal set; } = Array.Empty<RepositoryInfo>();
}
