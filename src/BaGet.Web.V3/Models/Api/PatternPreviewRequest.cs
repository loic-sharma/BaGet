using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>Asks what a set of patterns selects.</summary>
public sealed class PatternPreviewRequest
{
    /// <summary>Repository names or globs.</summary>
    public List<string> RepositoryPatterns { get; set; } = new();

    /// <summary>Package id globs.</summary>
    public List<string> IncludePatterns { get; set; } = new();

    /// <summary>Package id globs left out.</summary>
    public List<string> ExcludePatterns { get; set; } = new();

    /// <summary>A package id to test against the include and exclude patterns.</summary>
    public string? TestPackage { get; set; }
}
