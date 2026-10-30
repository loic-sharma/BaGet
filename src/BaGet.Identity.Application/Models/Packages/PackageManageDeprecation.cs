using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>A version's deprecation.</summary>
/// <param name="Reasons">Why (<c>Legacy</c>, <c>CriticalBugs</c>, <c>Other</c>).</param>
/// <param name="Message">Free text, or null.</param>
/// <param name="AlternatePackageId">The package to use instead, or null.</param>
/// <param name="AlternateVersionRange">The versions of it that may be used, or null.</param>
public sealed record PackageManageDeprecation(IReadOnlyList<string> Reasons, string? Message, string? AlternatePackageId, string? AlternateVersionRange);
