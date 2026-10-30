using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>The patterns of a permission target.</summary>
public sealed class PermissionTargetRequest
{
    /// <summary>The target's name (on create).</summary>
    public string? Name { get; set; }

    /// <summary>Repository names or <c>*</c> globs.</summary>
    public List<string> RepositoryPatterns { get; set; } = new();

    /// <summary>Package id globs; empty = every package.</summary>
    public List<string> IncludePatterns { get; set; } = new();

    /// <summary>Package id globs to leave out.</summary>
    public List<string> ExcludePatterns { get; set; } = new();

    /// <summary>The administering project's name (on create).</summary>
    public string? Project { get; set; }
}
