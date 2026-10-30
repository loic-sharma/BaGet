namespace BaGet.Web.V3.Models.Api;

/// <summary>Names a stored package of a remote repository to purge.</summary>
public sealed class PurgePackageRequest
{
    /// <summary>The package id (<c>group:artifact</c> for Maven, the image for Docker).</summary>
    public string? PackageId { get; set; }

    /// <summary>One version (a tag or digest for Docker); null purges every version.</summary>
    public string? Version { get; set; }
}
