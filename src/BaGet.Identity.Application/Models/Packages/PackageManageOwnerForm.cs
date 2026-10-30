namespace BaGet.Identity.Application.Models.Packages;

/// <summary>The form of a handler that names an owner.</summary>
public sealed class PackageManageOwnerForm
{
    /// <summary>The user or organization.</summary>
    public string? OwnerName { get; set; }
}
