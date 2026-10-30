using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>What the access audit screen (<c>/admin/audit</c>) shows: the filter and the matching entries.</summary>
public sealed class AuditViewModel
{
    /// <summary>The user name that made the change.</summary>
    public string? ActorFilter { get; set; }

    /// <summary>Part of what the change was done to (a group, target, project or repository name).</summary>
    public string? Subject { get; set; }

    /// <summary>First day, <c>yyyy-MM-dd</c>.</summary>
    public string? From { get; set; }

    /// <summary>Last day, <c>yyyy-MM-dd</c>.</summary>
    public string? To { get; set; }

    /// <summary>The matching entries.</summary>
    public IReadOnlyList<AccessAuditEntry> Entries { get; internal set; } = Array.Empty<AccessAuditEntry>();

    /// <summary>Why the filter was not applied, if a date could not be read.</summary>
    public string? FilterError { get; internal set; }
}
