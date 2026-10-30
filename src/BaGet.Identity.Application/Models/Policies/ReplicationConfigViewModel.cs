using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What one replication's screen (<c>/admin/replication/{key}</c>) shows.</summary>
public sealed class ReplicationConfigViewModel : PolicyViewModel
{
    /// <summary>The configuration's key (route).</summary>
    public int Key { get; set; }

    /// <summary>The settings, as edited.</summary>
    public ReplicationForm Form { get; set; } = new();

    /// <summary>The configuration with the names of what it uses.</summary>
    public ReplicationConfigView? View { get; internal set; }

    /// <summary>The log, newest first.</summary>
    public IReadOnlyList<ReplicationRun> Runs { get; internal set; } = Array.Empty<ReplicationRun>();

    /// <summary>What this process knows of the last job (a refusal that wrote no log entry shows here).</summary>
    public ReplicationJobStatus? Job { get; internal set; }

    /// <summary>The result of the last connection test on this request, or null.</summary>
    public ReplicationTestResult? Test { get; internal set; }

    /// <summary>How many items the local repository holds, or null when it could not be listed.</summary>
    public int? LocalItems { get; internal set; }

    /// <summary>Whether a run is going here (the page refreshes itself).</summary>
    public bool Running => Job?.Running == true;

    /// <summary>The local repositories a replication can name.</summary>
    public IReadOnlyList<RepositoryInfo> Repositories { get; internal set; } = Array.Empty<RepositoryInfo>();

    /// <summary>The newest run that ended without finishing everything, which <b>Resume</b> continues; null when there is none.</summary>
    public ReplicationRun? Resumable
    {
        get
        {
            var last = Runs.FirstOrDefault();

            return last != null && last.Status is ReplicationRunStatus.Failed or ReplicationRunStatus.Interrupted ? last : null;
        }
    }
}
