namespace BaGet.Identity.Application.Models.Access;

// Enum members read their names (like the answers write them): a body says "Group", not 2.
// The bodies of the access-control screens' writes (groups, permission targets, projects). A page posts them as form fields (the view controllers bind the
// same names and build these), the JSON API takes them as the request body; the use cases never see how they arrived.

/// <summary>A new group.</summary>
/// <param name="Name">The group's name.</param>
/// <param name="Description">What the group is for, or null.</param>
public sealed record GroupCreation(string? Name, string? Description);
