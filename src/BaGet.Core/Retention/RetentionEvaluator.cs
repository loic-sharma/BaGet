using System;
using System.Collections.Generic;
using System.Linq;

namespace BaGet.Core;

/// <summary>The rules of a <see cref="RetentionPolicy"/>, as the reasons a version is selected.</summary>
public enum RetentionRule
{
    /// <summary>Older than the newest <see cref="RetentionPolicy.KeepLast"/> versions of its artifact.</summary>
    KeepLast,

    /// <summary>Published longer ago than <see cref="RetentionPolicy.OlderThanDays"/>.</summary>
    OlderThan,

    /// <summary>Not downloaded for <see cref="RetentionPolicy.UnusedDays"/>.</summary>
    Unused,

    /// <summary>Evicted to bring a remote cache under <see cref="RetentionPolicy.CacheLimitBytes"/>.</summary>
    CacheLimit,

    /// <summary>An untagged Docker manifest older than <see cref="RetentionPolicy.UntaggedAfterDays"/>.</summary>
    Untagged,
}

/// <summary>A version a policy selected.</summary>
/// <param name="Name">The artifact.</param>
/// <param name="Version">The version (a digest for an untagged manifest).</param>
/// <param name="Published">When it was stored.</param>
/// <param name="LastDownloaded">The last download, when known.</param>
/// <param name="Size">Bytes it frees, when known.</param>
/// <param name="Rules">Every rule that selected it.</param>
/// <param name="Unreferenced">Whether it is an untagged manifest (listed by <see cref="IFormatRetentionHooks.ListUnreferencedAsync"/>).</param>
public sealed record RetentionSelection(string Name, string Version, DateTime Published, DateTime? LastDownloaded, long? Size, IReadOnlyList<RetentionRule> Rules, bool Unreferenced);

/// <summary>What <see cref="RetentionEvaluator.Evaluate"/> decided for one repository.</summary>
/// <param name="Examined">Versions looked at (after the name patterns).</param>
/// <param name="Selected">Versions to delete, in a stable order (artifact, then oldest first).</param>
/// <param name="Held">Versions a rule selected that a legal hold protects: they are not deleted.</param>
/// <param name="Notes">Rules that do not apply to this repository, in words.</param>
public sealed record RetentionDecision(int Examined, IReadOnlyList<RetentionSelection> Selected, IReadOnlyList<RetentionSelection> Held, IReadOnlyList<string> Notes);

/// <summary>
/// The retention rules as a pure function of a policy, the versions a repository stores, the legal holds and the clock: the dry run
/// and the real run both call it, so a report is exactly what a run deletes (a version held or deleted in between aside).
/// </summary>
public static class RetentionEvaluator
{
    /// <summary>The legal holds of a repository, matched case-insensitively. A hold with an empty version covers every version of the artifact.</summary>
    public sealed class HoldSet
    {
        private readonly HashSet<string> _artifacts;
        private readonly HashSet<(string, string)> _versions;

        /// <summary>No holds.</summary>
        public static HoldSet Empty { get; } = new(Array.Empty<(string, string)>());

        /// <param name="holds">(name, version) pairs; the version is empty for a whole artifact.</param>
        public HoldSet(IEnumerable<(string Name, string Version)> holds)
        {
            var all = holds.ToList();
            _artifacts = new HashSet<string>(all.Where(h => h.Version.Length == 0).Select(h => h.Name), StringComparer.OrdinalIgnoreCase);
            _versions = new HashSet<(string, string)>(all.Where(h => h.Version.Length > 0), PairComparer.Instance);
        }

        /// <summary>Whether the version (or its whole artifact) is under a legal hold.</summary>
        public bool IsHeld(string name, string version) => _artifacts.Contains(name) || _versions.Contains((name, version));

        private sealed class PairComparer : IEqualityComparer<(string, string)>
        {
            public static readonly PairComparer Instance = new();

            public bool Equals((string, string) x, (string, string) y)
                => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string, string) value)
                => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item2));
        }
    }

    /// <summary>Applies <paramref name="policy"/> to the versions of <paramref name="repository"/>.</summary>
    /// <param name="policy">The policy.</param>
    /// <param name="repository">The repository the versions are in (its kind decides whether the cache limit applies).</param>
    /// <param name="versions">Every version the format lists.</param>
    /// <param name="unreferenced">Things nothing points at (untagged Docker manifests).</param>
    /// <param name="holds">The legal holds.</param>
    /// <param name="now">The current time (UTC).</param>
    public static RetentionDecision Evaluate(
        RetentionPolicy policy,
        RepositoryInfo repository,
        IReadOnlyList<FormatVersion> versions,
        IReadOnlyList<FormatVersion> unreferenced,
        HoldSet holds,
        DateTime now)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        if (repository == null) throw new ArgumentNullException(nameof(repository));

        var notes = new List<string>();
        var patterns = AccessPatterns.Split(policy.NamePatterns);
        bool Matches(string name) => patterns.Length == 0 || AccessPatterns.IsMatch(patterns, name);

        var considered = versions.Where(v => Matches(v.Name)).ToList();
        var candidates = policy.PrereleaseOnly ? considered.Where(v => v.Prerelease).ToList() : considered;

        var rules = new Dictionary<(string, string), List<RetentionRule>>(PairKeyComparer.Instance);
        void Select(FormatVersion v, RetentionRule rule)
        {
            if (!rules.TryGetValue((v.Name, v.Version), out var list)) rules[(v.Name, v.Version)] = list = new List<RetentionRule>();
            if (!list.Contains(rule)) list.Add(rule);
        }

        if (policy.KeepLast is int keep && keep >= 0)
        {
            foreach (var artifact in candidates.GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            {
                // Newest first; the version text breaks a tie so the same data always gives the same answer.
                foreach (var old in artifact.OrderByDescending(v => v.Published).ThenByDescending(v => v.Version, StringComparer.Ordinal).Skip(keep)) Select(old, RetentionRule.KeepLast);
            }
        }

        if (policy.OlderThanDays is int age)
        {
            var cutoff = now - TimeSpan.FromDays(age);
            foreach (var v in candidates.Where(v => v.Published < cutoff)) Select(v, RetentionRule.OlderThan);
        }

        if (policy.UnusedDays is int unused)
        {
            var cutoff = now - TimeSpan.FromDays(unused);

            // A version with no recorded download counts from its publication, but never from before the policy watched its scope (UnusedSince, else Created): downloads
            // are recorded only since usage tracking began (an upgraded install has no history), so "unused for N days" cannot be
            // judged for a period the policy did not watch.
            foreach (var v in candidates.Where(v => (v.LastDownloaded ?? Latest(v.Published, policy.UnusedSince ?? policy.Created)) < cutoff)) Select(v, RetentionRule.Unused);
        }

        if (policy.CacheLimitBytes is long limit)
        {
            if (repository.Kind != RepositoryKind.Remote)
            {
                notes.Add("The cache limit applies to remote repositories only; it was ignored.");
            }
            else
            {
                // Evict the least recently used versions that no other rule and no hold already takes, until what is left fits.
                var total = considered.Sum(v => v.Size ?? 0);
                var freed = considered.Where(v => rules.ContainsKey((v.Name, v.Version)) && !holds.IsHeld(v.Name, v.Version)).Sum(v => v.Size ?? 0);
                if (total - freed > limit)
                {
                    foreach (var v in considered
                                 .Where(v => !rules.ContainsKey((v.Name, v.Version)) && !holds.IsHeld(v.Name, v.Version))
                                 .OrderBy(v => v.LastDownloaded ?? v.Published)
                                 .ThenBy(v => v.Name, StringComparer.Ordinal)
                                 .ThenBy(v => v.Version, StringComparer.Ordinal))
                    {
                        if (total - freed <= limit) break;

                        Select(v, RetentionRule.CacheLimit);
                        freed += v.Size ?? 0;
                    }
                }
            }
        }

        var selected = new List<RetentionSelection>();
        var held = new List<RetentionSelection>();
        foreach (var v in considered)
        {
            if (!rules.TryGetValue((v.Name, v.Version), out var list)) continue;

            var item = new RetentionSelection(v.Name, v.Version, v.Published, v.LastDownloaded, v.Size, list, Unreferenced: false);
            (holds.IsHeld(v.Name, v.Version) ? held : selected).Add(item);
        }

        if (policy.UntaggedAfterDays is int untagged)
        {
            if (repository.Format != RepositoryFormat.Docker)
            {
                notes.Add("The untagged-manifest rule applies to Docker repositories only; it was ignored.");
            }
            else
            {
                var cutoff = now - TimeSpan.FromDays(untagged);
                foreach (var v in unreferenced.Where(v => Matches(v.Name) && v.Published < cutoff))
                {
                    var item = new RetentionSelection(v.Name, v.Version, v.Published, v.LastDownloaded, v.Size, new[] { RetentionRule.Untagged }, Unreferenced: true);
                    (holds.IsHeld(v.Name, v.Version) ? held : selected).Add(item);
                }
            }
        }

        static IOrderedEnumerable<RetentionSelection> Order(IEnumerable<RetentionSelection> items)
            => items.OrderBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Published).ThenBy(i => i.Version, StringComparer.Ordinal);

        return new RetentionDecision(considered.Count, Order(selected).ToList(), Order(held).ToList(), notes);
    }

    private static DateTime Latest(DateTime a, DateTime b) => a > b ? a : b;

    private sealed class PairKeyComparer : IEqualityComparer<(string, string)>
    {
        public static readonly PairKeyComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.Ordinal) && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);

        public int GetHashCode((string, string) value) => HashCode.Combine(value.Item1, value.Item2);
    }
}
