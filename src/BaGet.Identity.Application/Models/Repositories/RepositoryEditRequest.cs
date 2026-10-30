namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>
/// The body of every action of the repository screen (<c>/api/ui/admin/repositories/{name}/...</c>), the JSON twin of what the page's forms post. Which fields
/// an action reads is stated on its use case method; the others are ignored.
/// </summary>
public sealed class RepositoryEditRequest
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

    /// <summary>The administrator's password (saving, testing and purging a remote repository ask for it).</summary>
    public string? Password { get; set; }

    /// <summary>The member to add, remove or move.</summary>
    public string? Member { get; set; }

    /// <summary><c>up</c> or <c>down</c>.</summary>
    public string? Direction { get; set; }

    /// <summary>The member that receives uploads (blank: none).</summary>
    public string? Deploy { get; set; }

    /// <summary>The versions a Maven repository accepts (<c>Releases</c>, <c>Snapshots</c>, <c>Both</c>).</summary>
    public string? Policy { get; set; }

    /// <summary>Whether a Maven release may be redeployed (<c>Yes</c>/<c>No</c>).</summary>
    public string? Redeploy { get; set; }

    /// <summary>How many snapshot builds a Maven repository keeps.</summary>
    public string? SnapshotBuilds { get; set; }
}
