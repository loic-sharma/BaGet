using System;
using System.Collections.Generic;

namespace BaGet.Web.Application.Models.Packages;

/// <summary>What the package page shows about the version it displays (a flat copy of the stored package, with no navigation properties).</summary>
public sealed record PackageDetails
{
    /// <summary>The package ID.</summary>
    public string? Id { get; init; }

    /// <summary>The normalized version.</summary>
    public string? Version { get; init; }

    /// <summary>The description.</summary>
    public string? Description { get; init; }

    /// <summary>The release notes.</summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>The project website, or an empty string.</summary>
    public string? ProjectUrl { get; init; }

    /// <summary>The source repository, or an empty string.</summary>
    public string? RepositoryUrl { get; init; }

    /// <summary>The downloads of this version.</summary>
    public long Downloads { get; init; }

    /// <summary>The authors.</summary>
    public IReadOnlyList<string> Authors { get; init; } = Array.Empty<string>();
}
