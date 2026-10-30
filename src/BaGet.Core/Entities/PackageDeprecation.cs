using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>
/// A package version marked as deprecated.
/// See: https://learn.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-deprecation
/// </summary>
public class PackageDeprecation
{
    /// <summary>
    /// The deprecation reasons the protocol defines.
    /// See: https://learn.microsoft.com/en-us/nuget/api/registration-base-url-resource#package-deprecation
    /// </summary>
    public static readonly IReadOnlyList<string> KnownReasons = new[] { "Legacy", "CriticalBugs", "Other" };

    public int Key { get; set; }

    /// <summary>
    /// Why the package was deprecated. The protocol defines "Legacy", "CriticalBugs"
    /// and "Other"; a package may carry more than one.
    /// </summary>
    public string[]? Reasons { get; set; }

    /// <summary>
    /// Free text explaining the deprecation. Optional.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// The ID of the package to use instead. Null if none was suggested.
    /// </summary>
    public string? AlternatePackageId { get; set; }

    /// <summary>
    /// The versions of the alternate package that may be used, or "*" for any.
    /// Null if no alternate package was suggested.
    /// </summary>
    public string? AlternateVersionRange { get; set; }

    /// <summary>
    /// The key of the deprecated package version. Nullable so that the relationship
    /// stays optional in the database: a required one would need a foreign key change,
    /// which SQLite cannot make in place.
    /// </summary>
    public int? PackageKey { get; set; }

    public Package? Package { get; set; }
}