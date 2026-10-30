namespace BaGet.Identity.Application.Models.Settings;

/// <summary>Removing a named OpenID Connect provider saved on the Settings screen.</summary>
/// <param name="Id">The provider id.</param>
/// <param name="Password">The administrator's password.</param>
public sealed record RemoveSignInProviderForm(string? Id, string? Password);
