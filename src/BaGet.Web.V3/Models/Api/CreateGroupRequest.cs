namespace BaGet.Web.V3.Models.Api;

/// <summary>Creates a group.</summary>
public sealed class CreateGroupRequest
{
    /// <summary>The group's name.</summary>
    public string? Name { get; set; }

    /// <summary>What the group is for.</summary>
    public string? Description { get; set; }
}
