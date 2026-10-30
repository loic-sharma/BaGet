namespace BaGet.Identity.Application.Models.Shared;

/// <summary>What the "access control unavailable" screen (<c>/admin/access-unavailable</c>) says: why, as the JSON carries it (the page has fixed text).</summary>
/// <param name="Reason">Why the access screens are not available.</param>
public sealed record AccessUnavailableViewModel(string Reason)
{
    /// <summary>The standard reason.</summary>
    public static AccessUnavailableViewModel Standard { get; } = new(
        "The configured database has no tables for groups, permission targets and projects. Use SQLite, SQL Server, PostgreSQL or MySQL to manage access here.");
}
