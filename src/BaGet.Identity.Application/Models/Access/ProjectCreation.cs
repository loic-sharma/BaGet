namespace BaGet.Identity.Application.Models.Access;

/// <summary>A new project.</summary>
/// <param name="Name">The project's name.</param>
/// <param name="DisplayName">A name to show, or null.</param>
public sealed record ProjectCreation(string? Name, string? DisplayName);
