namespace BaGet.Identity.Application.Models.Access;

/// <summary>Renaming a group.</summary>
/// <param name="NewName">The new name.</param>
public sealed record GroupRename(string? NewName);
