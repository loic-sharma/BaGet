namespace BaGet.Identity.Models.Packages;

/// <summary>The form of <c>Deprecate</c> and <c>Undeprecate</c>.</summary>
public sealed class NpmManageDeprecateForm
{
    /// <summary>The version.</summary>
    public string? Version { get; set; }

    /// <summary>The deprecation message (<c>Deprecate</c> only).</summary>
    public string? Message { get; set; }
}
