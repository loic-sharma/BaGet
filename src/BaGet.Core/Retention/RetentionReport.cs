using System;
using System.Collections.Generic;
using System.Linq;

namespace BaGet.Core;

/// <summary>What happened (or, in a dry run, would happen) to one selected version.</summary>
public enum RetentionOutcome
{
    /// <summary>Dry run: the run would delete it.</summary>
    Planned,

    /// <summary>Deleted.</summary>
    Deleted,

    /// <summary>A rule selected it and a legal hold protects it: kept.</summary>
    Held,

    /// <summary>Deleting it failed (<see cref="RetentionReportItem.Error"/>); the next run tries again.</summary>
    Failed,
}

/// <summary>One version in a report.</summary>
public sealed record RetentionReportItem(
    string Repository,
    string Name,
    string Version,
    DateTime Published,
    DateTime? LastDownloaded,
    long? Size,
    string Rules,
    RetentionOutcome Outcome,
    string? Error);

/// <summary>The part of a <see cref="RetentionReport"/> that concerns one repository.</summary>
public sealed class RetentionRepositoryReport
{
    /// <summary>The most items listed per repository (the counts are exact).</summary>
    public const int MaxItems = 1000;

    public string Repository { get; init; } = string.Empty;

    public RepositoryFormat Format { get; init; }

    /// <summary>Versions looked at (matching the policy's name patterns).</summary>
    public int Examined { get; set; }

    /// <summary>Versions selected by a rule and not held (dry run: to be deleted).</summary>
    public int Selected { get; set; }

    /// <summary>Versions deleted (always 0 in a dry run).</summary>
    public int Deleted { get; set; }

    /// <summary>Versions a rule selected that a legal hold kept.</summary>
    public int Held { get; set; }

    /// <summary>Versions whose deletion failed.</summary>
    public int Failed { get; set; }

    /// <summary>Bytes of the deleted versions (dry run: of the selected ones), as far as known.</summary>
    public long Bytes { get; set; }

    public List<RetentionReportItem> Items { get; } = new();

    public bool ItemsTruncated { get; set; }

    /// <summary>Rules that did not apply, and similar remarks.</summary>
    public List<string> Notes { get; } = new();

    /// <summary>For Docker, the garbage collection that followed (a dry run: what it would remove, as of before this run's deletions).</summary>
    public OciGcReport? GarbageCollection { get; set; }

    /// <summary>Why the repository could not be processed, or null.</summary>
    public string? Error { get; set; }

    internal void Add(RetentionReportItem item)
    {
        if (Items.Count < MaxItems) Items.Add(item);
        else ItemsTruncated = true;
    }
}

/// <summary>The result of running (or dry-running) a policy over its repositories.</summary>
public sealed class RetentionReport
{
    public int PolicyKey { get; init; }

    public string PolicyName { get; init; } = string.Empty;

    /// <summary>Whether nothing was changed.</summary>
    public bool DryRun { get; init; }

    public DateTime Started { get; init; }

    public DateTime? Finished { get; set; }

    public List<RetentionRepositoryReport> Repositories { get; } = new();

    public int Selected => Repositories.Sum(r => r.Selected);

    public int Deleted => Repositories.Sum(r => r.Deleted);

    public int Held => Repositories.Sum(r => r.Held);

    public int Failed => Repositories.Sum(r => r.Failed);

    public long Bytes => Repositories.Sum(r => r.Bytes);

    /// <summary>One line for the policy page, the job status and the audit log.</summary>
    public string Summary
    {
        get
        {
            var text = DryRun
                ? $"Dry run, nothing deleted. Would delete {Selected} version(s) ({FormatBytes(Bytes)}), {Held} kept by legal hold"
                : $"Deleted {Deleted} version(s) ({FormatBytes(Bytes)}), {Held} kept by legal hold, {Failed} failed";
            text += $", across {Repositories.Count} repositor{(Repositories.Count == 1 ? "y" : "ies")}.";
            var errors = Repositories.Where(r => r.Error != null).Select(r => $"{r.Repository}: {r.Error}").ToList();
            if (errors.Count > 0) text += " Errors: " + string.Join("; ", errors) + ".";

            return text;
        }
    }

    /// <summary>Bytes in KiB/MiB/GiB for the pages.</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
