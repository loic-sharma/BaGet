namespace BaGet.Identity.Application.Models.Users;

/// <summary>Adding an account to a group or removing it.</summary>
/// <param name="Group">The group's name.</param>
/// <param name="Password">The administrator's password.</param>
public sealed record UserGroupForm(string? Group, string? Password);
