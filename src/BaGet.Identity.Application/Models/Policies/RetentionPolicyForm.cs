using System;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>
/// What the policy form posts (<c>Form.*</c>): the rules of a <see cref="RetentionPolicy"/> in the units a person types
/// (the cache limit in MiB). <see cref="ToInput"/> turns it into the administration's input.
/// </summary>
public sealed class RetentionPolicyForm
{
    public string? Name { get; set; }

    /// <summary><c>repository</c> or <c>project</c>.</summary>
    public string? ScopeKind { get; set; } = "repository";

    /// <summary>The repository or project name.</summary>
    public string? ScopeName { get; set; }

    public bool Enabled { get; set; }

    public string? NamePatterns { get; set; }

    public int? KeepLast { get; set; }

    public int? OlderThanDays { get; set; }

    public int? UnusedDays { get; set; }

    public bool PrereleaseOnly { get; set; }

    /// <summary>The remote cache limit in MiB.</summary>
    public long? CacheLimitMb { get; set; }

    public int? UntaggedAfterDays { get; set; }

    public bool CollectGarbage { get; set; }

    public int? ScheduleHours { get; set; }

    /// <summary>The administration's input for policy <paramref name="key"/> (0 creates).</summary>
    public RetentionPolicyInput ToInput(int key)
    {
        var project = string.Equals(ScopeKind, "project", StringComparison.Ordinal);
        long? limit = CacheLimitMb is long mb ? (mb > long.MaxValue / (1024 * 1024) ? long.MaxValue : mb * 1024 * 1024) : null;

        return new RetentionPolicyInput(
            key, Name, project ? null : ScopeName, project ? ScopeName : null, Enabled, NamePatterns,
            KeepLast, OlderThanDays, UnusedDays, PrereleaseOnly, limit, UntaggedAfterDays, CollectGarbage, ScheduleHours);
    }

    /// <summary>The form filled in from a stored policy.</summary>
    public static RetentionPolicyForm From(RetentionPolicyView view)
    {
        var p = view.Policy;

        return new RetentionPolicyForm
        {
            Name = p.Name,
            ScopeKind = p.ProjectKey != null ? "project" : "repository",
            ScopeName = view.Repository ?? view.Project,
            Enabled = p.Enabled,
            NamePatterns = p.NamePatterns,
            KeepLast = p.KeepLast,
            OlderThanDays = p.OlderThanDays,
            UnusedDays = p.UnusedDays,
            PrereleaseOnly = p.PrereleaseOnly,
            CacheLimitMb = p.CacheLimitBytes is long bytes ? (bytes + 1024 * 1024 - 1) / (1024 * 1024) : null,
            UntaggedAfterDays = p.UntaggedAfterDays,
            CollectGarbage = p.CollectGarbage,
            ScheduleHours = p.ScheduleHours,
        };
    }
}
