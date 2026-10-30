using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BaGet.Core;
using BaGet.Identity.Application.Models.Policies;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>What the permission targets screen (<c>/admin/permissions</c>) shows: the targets and the new-target form.</summary>
public sealed class PermissionsViewModel : AccessViewModel
{
    /// <summary>The new target name.</summary>
    public string? Name { get; set; }

    /// <summary>Repository names or <c>*</c> globs, one per line.</summary>
    public string? RepositoryPatterns { get; set; }

    /// <summary>Package ID globs the target applies to, one per line; none means all.</summary>
    public string? IncludePatterns { get; set; }

    /// <summary>Package ID globs the target leaves out, one per line.</summary>
    public string? ExcludePatterns { get; set; }

    /// <summary>The project that administers the target, if any.</summary>
    public string? Project { get; set; }

    /// <summary>A package ID to test the patterns with.</summary>
    public string? TestPackage { get; set; }

    /// <summary>The targets.</summary>
    public IReadOnlyList<TargetSummary> Targets { get; internal set; } = Array.Empty<TargetSummary>();

    /// <summary>The projects a target can belong to.</summary>
    public IReadOnlyList<ProjectSummary> Projects { get; internal set; } = Array.Empty<ProjectSummary>();

    /// <summary>The preview of the form patterns.</summary>
    public PatternPreview? Preview { get; internal set; }

    /// <summary>The model of the pattern fields partial.</summary>
    [JsonIgnore]
    public PatternFieldsView Fields => new(RepositoryPatterns, IncludePatterns, ExcludePatterns, TestPackage, Preview);
}
