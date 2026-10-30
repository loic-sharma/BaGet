namespace BaGet.Web.V3.Models.Api;

/// <summary>Places a legal hold.</summary>
public sealed class HoldRequest
{
    /// <summary>The repository.</summary>
    public string? Repository { get; set; }

    /// <summary>The package, artifact or image name.</summary>
    public string? Name { get; set; }

    /// <summary>One version; empty holds every version.</summary>
    public string? Version { get; set; }

    /// <summary>Why.</summary>
    public string? Reason { get; set; }
}
