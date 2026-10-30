using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BaGet.Core;
using BaGet.Identity.Application.Models.Policies;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>What the project screen (<c>/admin/projects/{name}</c>) shows: repositories, members and the project targets.</summary>
public sealed class ProjectDetailViewModel : AccessViewModel
{
    /// <summary>The project name (route).</summary>
    public string? Name { get; set; }

    /// <summary>The project.</summary>
    public ProjectDetails? Project { get; internal set; }

    /// <summary>The NuGet repositories not yet in this project, for the assignment picker.</summary>
    public IReadOnlyList<string> Assignable { get; internal set; } = Array.Empty<string>();

    /// <summary>The new target name.</summary>
    public string? TargetName { get; set; }

    /// <summary>Repository globs of the new target, one per line (they only ever reach the project repositories).</summary>
    public string? RepositoryPatterns { get; set; }

    /// <summary>Include globs of the new target.</summary>
    public string? IncludePatterns { get; set; }

    /// <summary>Exclude globs of the new target.</summary>
    public string? ExcludePatterns { get; set; }

    /// <summary>A package ID to test the patterns with.</summary>
    public string? TestPackage { get; set; }

    /// <summary>What the new target patterns select.</summary>
    public PatternPreview? Preview { get; internal set; }

    /// <summary>The model of the pattern fields partial.</summary>
    [JsonIgnore]
    public PatternFieldsView Fields => new(RepositoryPatterns, IncludePatterns, ExcludePatterns, TestPackage, Preview);
}
