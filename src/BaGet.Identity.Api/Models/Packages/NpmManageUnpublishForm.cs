namespace BaGet.Identity.Models.Packages;

/// <summary>The form of <c>Unpublish</c> (one version) and <c>UnpublishAll</c> (the version is ignored); the password confirms.</summary>
public sealed class NpmManageUnpublishForm
{
    /// <summary>The version (<c>Unpublish</c> only).</summary>
    public string? Version { get; set; }

    /// <summary>The caller's password.</summary>
    public string? Password { get; set; }
}
