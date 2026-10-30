namespace BaGet.Identity.Application.Models.Users;

/// <summary>Deleting an account.</summary>
/// <param name="SoleOwned">"keep" (default) or "delete": what to do with the package IDs only this user owns.</param>
/// <param name="Password">The administrator's password.</param>
public sealed record UserDeletionForm(string? SoleOwned, string? Password);
