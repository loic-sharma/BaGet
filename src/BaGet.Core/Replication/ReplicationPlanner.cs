using System;
using System.Collections.Generic;
using System.Linq;

namespace BaGet.Core;

/// <summary>What a run will do, worked out from both inventories and the ledger by <see cref="ReplicationPlanner.Plan"/>.</summary>
public sealed class ReplicationPlan
{
    /// <summary>Source items the target lacks: copy them.</summary>
    public List<ReplicaItem> Copy { get; } = new();

    /// <summary>Items held with different content on both sides while the source wins: delete at the target, then copy.</summary>
    public List<ReplicaItem> Replace { get; } = new();

    /// <summary>Items held with different content on both sides while the target wins: left alone, logged.</summary>
    public List<ReplicaItem> Conflicts { get; } = new();

    /// <summary>Items copied earlier that the source no longer holds but the target still does (delete propagation).</summary>
    public List<ReplicaItem> Delete { get; } = new();

    /// <summary>Ledger entries to drop: the item is gone from both sides, or from the source while propagation is off.</summary>
    public List<ReplicaItem> Forget { get; } = new();

    /// <summary>Items the target holds already with the same content.</summary>
    public int Skipped { get; set; }

    /// <summary>Items looked at: every source item plus every target-only ledger item.</summary>
    public int Examined { get; set; }
}

/// <summary>
/// The decision part of a run, free of I/O so it can be tested exhaustively: what to copy, replace, leave, delete or forget.
/// </summary>
public static class ReplicationPlanner
{
    /// <summary>The identity of an item: name (compared the format's way), version and part.</summary>
    public static IEqualityComparer<ReplicaItem> KeyComparer(StringComparer names) => new ItemKeyComparer(names);

    /// <summary>Works out the plan.</summary>
    /// <param name="source">What the sending side holds.</param>
    /// <param name="target">What the receiving side holds.</param>
    /// <param name="ledger">What this configuration copied earlier (its ledger).</param>
    /// <param name="names">How the format compares names.</param>
    /// <param name="conflict">What to do when both sides hold the item with different content.</param>
    /// <param name="propagateDeletes">Whether an item copied earlier and gone from the source is deleted at the target.</param>
    public static ReplicationPlan Plan(
        IReadOnlyCollection<ReplicaItem> source,
        IReadOnlyCollection<ReplicaItem> target,
        IReadOnlyCollection<ReplicaItem> ledger,
        StringComparer names,
        ReplicationConflict conflict,
        bool propagateDeletes)
    {
        var comparer = new ItemKeyComparer(names);
        var sourceByKey = source.GroupBy(i => i, comparer).ToDictionary(g => g.Key, g => g.First(), comparer);
        var targetByKey = target.GroupBy(i => i, comparer).ToDictionary(g => g.Key, g => g.First(), comparer);
        var plan = new ReplicationPlan();

        foreach (var item in sourceByKey.Values.OrderBy(i => i.Name, names).ThenBy(i => i.Version, StringComparer.Ordinal).ThenBy(i => i.Part, StringComparer.Ordinal))
        {
            plan.Examined++;
            if (!targetByKey.TryGetValue(item, out var held)) plan.Copy.Add(item);
            else if (!item.DiffersFrom(held)) plan.Skipped++;
            else if (conflict == ReplicationConflict.SourceWins) plan.Replace.Add(item);
            else plan.Conflicts.Add(item);
        }

        foreach (var entry in ledger)
        {
            if (sourceByKey.ContainsKey(entry)) continue;

            plan.Examined++;
            if (propagateDeletes && targetByKey.TryGetValue(entry, out var held))
            {
                // Content the target changed after it was replicated is deleted only when the source wins; else it is left alone.
                if (!entry.DiffersFrom(held) || conflict == ReplicationConflict.SourceWins) plan.Delete.Add(held);
                else plan.Forget.Add(entry);
            }
            else plan.Forget.Add(entry);
        }

        return plan;
    }

    private sealed class ItemKeyComparer : IEqualityComparer<ReplicaItem>
    {
        private readonly StringComparer _names;

        public ItemKeyComparer(StringComparer names)
        {
            _names = names;
        }

        public bool Equals(ReplicaItem? x, ReplicaItem? y)
            => x != null && y != null
               && _names.Equals(x.Name, y.Name)
               && string.Equals(x.Version, y.Version, StringComparison.Ordinal)
               && string.Equals(x.Part, y.Part, StringComparison.Ordinal);

        public int GetHashCode(ReplicaItem item) => HashCode.Combine(_names.GetHashCode(item.Name), item.Version, item.Part);
    }
}
