namespace BaGet.Identity.Application.Models.Packages;

/// <summary>
/// The columns the summary needs, and nothing else.
/// </summary>
internal class OwnedVersion
{
    public string? Id { get; set; }

    public string? NormalizedVersion { get; set; }

    public string? OriginalVersion { get; set; }

    public bool Listed { get; set; }

    public long Downloads { get; set; }
}
