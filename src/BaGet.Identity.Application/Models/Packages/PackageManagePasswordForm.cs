namespace BaGet.Identity.Application.Models.Packages;

/// <summary>The form of a handler that only asks for the password.</summary>
public sealed class PackageManagePasswordForm
{
    /// <summary>The caller's password.</summary>
    public string? Password { get; set; }
}
