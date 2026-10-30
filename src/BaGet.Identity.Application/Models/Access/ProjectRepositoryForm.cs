namespace BaGet.Identity.Application.Models.Access;

/// <summary>Putting a repository in a project or taking it out.</summary>
/// <param name="Repository">The repository's name.</param>
/// <param name="Password">The administrator's password, asked again.</param>
public sealed record ProjectRepositoryForm(string? Repository, string? Password);
