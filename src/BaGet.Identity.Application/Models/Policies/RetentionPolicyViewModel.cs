using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What one retention policy's screen (<c>/admin/retention/{key}</c>) shows.</summary>
public sealed class RetentionPolicyViewModel : PolicyViewModel
{
    /// <summary>The policy's key (route).</summary>
    public int Key { get; set; }

    /// <summary>The rules, as edited.</summary>
    public RetentionPolicyForm Form { get; set; } = new();

    /// <summary>The policy with the names of what it applies to.</summary>
    public RetentionPolicyView? View { get; internal set; }

    /// <summary>The last run since the server started (a dry run's report included), or null.</summary>
    public RetentionJobStatus? Status { get; internal set; }

    /// <summary>Whether a run is in progress (the page refreshes itself).</summary>
    public bool Running => Status?.State == RetentionJobState.Running;

    /// <summary>The repositories a policy can name.</summary>
    public IReadOnlyList<RepositoryInfo> Repositories { get; internal set; } = Array.Empty<RepositoryInfo>();

    /// <summary>The names of the projects a policy can name.</summary>
    public IReadOnlyList<string> ProjectNames { get; internal set; } = Array.Empty<string>();
}
