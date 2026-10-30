using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>What the groups screen (<c>/admin/groups</c>) shows: the groups and the new-group form.</summary>
public sealed class GroupsViewModel : AccessViewModel
{
    /// <summary>The new group name.</summary>
    public string? Name { get; set; }

    /// <summary>The new group description.</summary>
    public string? Description { get; set; }

    /// <summary>The groups.</summary>
    public IReadOnlyList<GroupSummary> Groups { get; internal set; } = Array.Empty<GroupSummary>();
}
