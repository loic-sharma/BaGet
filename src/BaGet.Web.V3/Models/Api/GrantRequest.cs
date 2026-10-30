using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>Gives a principal actions through a target.</summary>
public sealed class GrantRequest
{
    /// <summary><c>User</c>, <c>Group</c> or <c>Organization</c>.</summary>
    public string? Type { get; set; }

    /// <summary>The principal's name.</summary>
    public string? Principal { get; set; }

    /// <summary>Some of <c>Read</c>, <c>Annotate</c>, <c>Deploy</c>, <c>Delete</c>, <c>Manage</c>; none removes the grant.</summary>
    public List<string> Actions { get; set; } = new();
}
