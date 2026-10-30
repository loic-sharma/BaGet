using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>What the delete-repository screen (<c>/admin/repositories/{name}/delete</c>) shows.</summary>
public sealed class RepositoryDeleteViewModel
{
    /// <summary>The repository's name (route).</summary>
    public string? Name { get; set; }

    /// <summary>The repository.</summary>
    public RepositorySummary? Repository { get; internal set; }

    /// <summary>The virtual repositories that list this repository as a member (the deletion is refused while there are any).</summary>
    public IReadOnlyList<string> ReferencedBy { get; internal set; } = new List<string>();

    /// <summary>Why the deletion was refused.</summary>
    public string? Error { get; internal set; }
}
