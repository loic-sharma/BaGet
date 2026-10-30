namespace BaGet.Identity.Application.Models.Users;

/// <summary>Revoking one of an account's API keys.</summary>
/// <param name="Key">The key's number (<see cref="UserKeyRow.Key"/>).</param>
/// <param name="Password">The administrator's password.</param>
public sealed record UserKeyForm(int Key, string? Password);
