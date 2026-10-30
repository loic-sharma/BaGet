namespace BaGet.Web.V3.Models.Api;

/// <summary>Creates a project.</summary>
public sealed class CreateProjectRequest
{
    /// <summary>The project key (lowercase letters, digits and hyphens).</summary>
    public string? Name { get; set; }

    /// <summary>A name for people.</summary>
    public string? DisplayName { get; set; }
}
