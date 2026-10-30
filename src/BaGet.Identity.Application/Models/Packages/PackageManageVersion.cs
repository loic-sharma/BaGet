using System.Collections.Generic;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>One version of the ID.</summary>
/// <param name="Version">The version as published (original text).</param>
/// <param name="NormalizedVersion">The normalized version, which the forms post back.</param>
/// <param name="Downloads">The download count.</param>
/// <param name="Listed">Whether the version is listed.</param>
/// <param name="Deprecation">Its deprecation, or null.</param>
/// <param name="Vulnerabilities">Its vulnerabilities.</param>
public sealed record PackageManageVersion(
    string Version,
    string? NormalizedVersion,
    long Downloads,
    bool Listed,
    PackageManageDeprecation? Deprecation,
    IReadOnlyList<PackageManageVulnerability> Vulnerabilities);
