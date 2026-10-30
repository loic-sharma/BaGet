namespace BaGet.Identity.Application.Models.Packages;

/// <summary>
/// One owned package ID, summarised across its versions.
/// </summary>
public class OwnedPackage
{
    public string? Id { get; set; }

    /// <summary>
    /// The highest version, listed or not, or null when no version remains.
    /// </summary>
    public string? LatestVersion { get; set; }

    public bool LatestVersionIsListed { get; set; }

    public int VersionCount { get; set; }

    public long Downloads { get; set; }
}
