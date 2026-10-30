namespace BaGet.Identity.Application.Models.Packages;

/// <summary>The form of a handler that names a version.</summary>
public sealed class PackageManageVersionForm
{
    /// <summary>The (normalized) version.</summary>
    public string? Version { get; set; }
}
