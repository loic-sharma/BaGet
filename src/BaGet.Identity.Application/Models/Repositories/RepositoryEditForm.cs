namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>
/// The fields the repository screen's forms post (every handler binds the same set). The upstream fields come with the <c>Remote.</c> prefix.
/// </summary>
public sealed class RepositoryEditForm
{
    /// <summary>Whether requests without credentials may read the repository.</summary>
    public bool AnonymousRead { get; set; }

    /// <summary>The upstream fields of a remote repository.</summary>
    public RemoteFormModel Remote { get; set; } = new();

    /// <summary>The package id to resolve in the preview.</summary>
    public string? PreviewPackageId { get; set; }

    /// <summary>The package version to resolve in the preview.</summary>
    public string? PreviewVersion { get; set; }

    /// <summary>The package id to purge from the cache.</summary>
    public string? PackageId { get; set; }

    /// <summary>The version to purge (blank = every version).</summary>
    public string? PackageVersion { get; set; }
}
