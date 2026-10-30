using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;

namespace BaGet.Identity.Application.UseCases.Policies;

/// <summary>
/// What the retention, scanning and replication use cases share: the lists their forms choose from and the one-line descriptions of
/// a policy or a replication (also used by the views through <c>RetentionControllerBase.Describe</c> / <c>ReplicationControllerBase.Describe</c>).
/// </summary>
internal static class PolicyScreenSupport
{
    /// <summary>The repositories a retention policy or hold can name (virtual ones store nothing), by name.</summary>
    public static async Task<IReadOnlyList<RepositoryInfo>> RetentionRepositoriesAsync(IRepositoryService repositories, CancellationToken cancellationToken)
        => (await repositories.ListAsync(cancellationToken))
            .Where(r => r.Kind != RepositoryKind.Virtual).OrderBy(r => r.Name, StringComparer.Ordinal).ToList();

    /// <summary>The names of the projects a policy can name.</summary>
    public static async Task<IReadOnlyList<string>> ProjectNamesAsync(IAccessAdministration access, CancellationToken cancellationToken)
        => (await access.ListProjectsAsync(cancellationToken)).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The local repositories a replication can name (remote and virtual ones are not replicated), by name.</summary>
    public static async Task<IReadOnlyList<RepositoryInfo>> ReplicationRepositoriesAsync(IRepositoryService repositories, CancellationToken cancellationToken)
        => (await repositories.ListAsync(cancellationToken))
            .Where(r => r.Kind == RepositoryKind.Local).OrderBy(r => r.Name, StringComparer.Ordinal).ToList();

    /// <summary>The schedule and triggers of a configuration in a few words.</summary>
    public static string DescribeReplication(ReplicationConfig c)
    {
        var parts = new List<string> { c.Direction == ReplicationDirection.Push ? "push" : "pull" };
        if (c.OnDeploy) parts.Add("on deploy");
        parts.Add(c.ScheduleMinutes is int minutes ? "every " + minutes + " min" : "manual");
        if (c.PropagateDeletes) parts.Add("deletes follow");
        parts.Add(c.Conflict == ReplicationConflict.SourceWins ? "source wins" : "target wins");

        return string.Join(", ", parts);
    }

    /// <summary>The rules of a policy in one line, for the lists.</summary>
    public static string DescribeRetention(RetentionPolicy p)
    {
        var rules = new List<string>();
        if (p.KeepLast is int keep) rules.Add($"keep the newest {keep}");
        if (p.OlderThanDays is int age) rules.Add($"older than {age} d");
        if (p.UnusedDays is int unused) rules.Add($"unused for {unused} d");
        if (p.CacheLimitBytes is long cache) rules.Add($"cache over {RetentionReport.FormatBytes(cache)}");
        if (p.UntaggedAfterDays is int untagged) rules.Add($"untagged for {untagged} d");
        if (p.CollectGarbage) rules.Add("garbage collection");
        var text = rules.Count == 0 ? "no rule" : string.Join(", ", rules);
        if (p.PrereleaseOnly) text += " (prerelease only)";

        return text;
    }
}
