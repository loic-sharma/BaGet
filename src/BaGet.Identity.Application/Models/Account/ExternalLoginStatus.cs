namespace BaGet.Identity.Application.Models.Account;

/// <summary>A configured external provider as the account settings list it for one user.</summary>
/// <param name="Id">The provider id.</param>
/// <param name="DisplayName">The provider's name.</param>
/// <param name="Linked">Whether the account has a linked login at this provider.</param>
public sealed record ExternalLoginStatus(string Id, string DisplayName, bool Linked);
