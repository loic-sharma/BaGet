namespace BaGet.Identity.Models.Packages;

/// <summary>The form of the Docker management handlers; every one asks for the caller's password.</summary>
public sealed class DockerManageForm
{
    /// <summary>The tag to delete (<c>DeleteTag</c>).</summary>
    public string? Tag { get; set; }

    /// <summary>The manifest digest to delete (<c>DeleteManifest</c>).</summary>
    public string? Digest { get; set; }

    /// <summary>The caller's password.</summary>
    public string? Password { get; set; }
}
