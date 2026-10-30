using System;
using NuGet.Versioning;

namespace BaGet.Web.V3.ViewModels;

/// <summary>One row of the versions table of the package page.</summary>
public sealed record PackageVersionRow
{
    /// <summary>The version.</summary>
    public NuGetVersion? Version { get; init; }

    /// <summary>Its downloads.</summary>
    public long Downloads { get; init; }

    /// <summary>Whether it is the version shown.</summary>
    public bool Selected { get; init; }

    /// <summary>When it was published.</summary>
    public DateTime LastUpdated { get; init; }
}
