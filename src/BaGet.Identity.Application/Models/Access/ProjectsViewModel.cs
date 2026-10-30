using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>What the projects screen (<c>/admin/projects</c>) shows: the projects the user may open and the new-project form.</summary>
public sealed class ProjectsViewModel : AccessViewModel
{
    /// <summary>The new project name.</summary>
    public string? Name { get; set; }

    /// <summary>The new project display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The projects the signed-in user may open.</summary>
    public IReadOnlyList<ProjectSummary> Projects { get; internal set; } = Array.Empty<ProjectSummary>();
}
