using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the retention policies screen (<c>/admin/retention</c>) shows: every policy and the form that creates one.</summary>
public sealed class RetentionViewModel : PolicyViewModel
{
    /// <summary>The new policy.</summary>
    public RetentionPolicyForm Form { get; set; } = new();

    /// <summary>Every policy.</summary>
    public IReadOnlyList<RetentionPolicyView> Policies { get; internal set; } = Array.Empty<RetentionPolicyView>();

    /// <summary>The repositories a policy can name.</summary>
    public IReadOnlyList<RepositoryInfo> Repositories { get; internal set; } = Array.Empty<RepositoryInfo>();

    /// <summary>The names of the projects a policy can name.</summary>
    public IReadOnlyList<string> ProjectNames { get; internal set; } = Array.Empty<string>();
}
