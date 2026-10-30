namespace BaGet.Core;

/// <summary>
/// Which protocol surfaces BaGet exposes.
/// </summary>
public class FeedsOptions
{
    /// <summary>
    /// Whether the NuGet V2 (OData) feed is served under "api/v2".
    /// </summary>
    /// <remarks>
    /// On by default: clients that cannot speak V3 - "nuget list", older
    /// Install-Package, and PowerShellGet - have no other way in.
    /// </remarks>
    public bool EnableV2 { get; set; } = true;
}