using System.Text.Json.Serialization;
using BaGet.Core;
using BaGet.Identity.Application.Models.Policies;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>What the permission target screen (<c>/admin/permissions/{name}</c>) shows: the patterns, the preview and the grants.</summary>
public sealed class PermissionDetailViewModel : AccessViewModel
{
    /// <summary>The target name (route).</summary>
    public string? Name { get; set; }

    /// <summary>Repository names or globs, one per line.</summary>
    public string? RepositoryPatterns { get; set; }

    /// <summary>Package ID globs the target applies to, one per line.</summary>
    public string? IncludePatterns { get; set; }

    /// <summary>Package ID globs the target leaves out, one per line.</summary>
    public string? ExcludePatterns { get; set; }

    /// <summary>A package ID to test the patterns with.</summary>
    public string? TestPackage { get; set; }

    /// <summary>The kind of principal in the grant form.</summary>
    public PrincipalType PrincipalType { get; set; } = PrincipalType.Group;

    /// <summary>The user, group or organization name in the grant form.</summary>
    public string? PrincipalName { get; set; }

    /// <summary>The target.</summary>
    public TargetDetails? Target { get; internal set; }

    /// <summary>What the patterns select.</summary>
    public PatternPreview? Preview { get; internal set; }

    /// <summary>The actions ticked in the grant form.</summary>
    public AccessAction FormActions { get; internal set; }

    /// <summary>The model of the pattern fields partial.</summary>
    [JsonIgnore]
    public PatternFieldsView Fields => new(RepositoryPatterns, IncludePatterns, ExcludePatterns, TestPackage, Preview);
}
