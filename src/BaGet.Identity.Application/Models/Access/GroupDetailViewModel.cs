using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>What the group screen (<c>/admin/groups/{name}</c>) shows: the group, its members and the member search.</summary>
public sealed class GroupDetailViewModel : AccessViewModel
{
    /// <summary>The group name (route).</summary>
    public string? Name { get; set; }

    /// <summary>User-name search for the member picker (contains).</summary>
    public string? Q { get; set; }

    /// <summary>The group.</summary>
    public GroupDetails? Group { get; internal set; }

    /// <summary>The users found by <see cref="Q"/>, not yet members.</summary>
    public IReadOnlyList<GroupMemberInfo> Found { get; internal set; } = Array.Empty<GroupMemberInfo>();
}
