namespace BaGet.Web.V3.Models.Api;

/// <summary>Renames a group.</summary>
public sealed class RenameGroupRequest
{
    /// <summary>The new name (the built-in groups keep theirs).</summary>
    public string? NewName { get; set; }
}
