namespace BaGet.Identity.Application.Models.Account;

/// <summary>An external sign-in provider as a screen offers it (one button per provider).</summary>
/// <param name="Id">The provider id: the value a form or <c>/api/ui/session/external/{provider}</c> names it by.</param>
/// <param name="DisplayName">The name on the button.</param>
public sealed record ExternalProviderInfo(string Id, string DisplayName);
