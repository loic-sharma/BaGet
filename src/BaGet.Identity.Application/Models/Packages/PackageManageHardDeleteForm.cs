namespace BaGet.Identity.Application.Models.Packages;

/// <summary>The form of a permanent deletion of a version (administrators; the password confirms).</summary>
public sealed class PackageManageHardDeleteForm
{
    /// <summary>The version.</summary>
    public string? Version { get; set; }

    /// <summary>The caller's password.</summary>
    public string? Password { get; set; }
}
