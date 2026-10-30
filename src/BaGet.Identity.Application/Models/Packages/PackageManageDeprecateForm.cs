using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>The form of <c>Deprecate</c>.</summary>
public sealed class PackageManageDeprecateForm
{
    /// <summary>The version.</summary>
    public string? Version { get; set; }

    /// <summary>The reasons (at least one of <c>Legacy</c>, <c>CriticalBugs</c>, <c>Other</c>).</summary>
    public List<string>? Reasons { get; set; }

    /// <summary>Free text, optional.</summary>
    public string? Message { get; set; }

    /// <summary>The package to use instead, optional.</summary>
    public string? AlternatePackageId { get; set; }

    /// <summary>The versions of it that may be used, optional (needs <see cref="AlternatePackageId"/>).</summary>
    public string? AlternateVersionRange { get; set; }
}
