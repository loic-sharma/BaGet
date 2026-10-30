namespace BaGet.Identity.Application.Models.Settings;

/// <summary>
/// What the "Sign-in providers" panel of the Settings screen posts to add or edit a named OpenID Connect provider (the body of
/// <c>POST /api/ui/admin/settings/save-provider</c>). The id is the key and cannot change once created; a blank secret keeps the saved one.
/// <see cref="Parameters"/> and <see cref="GroupMappings"/> are one <c>name=value</c> per line.
/// </summary>
public sealed class SignInProviderForm
{
    /// <summary>The provider id (lower-case letters, digits and <c>-</c>, 1 to 32).</summary>
    public string? Id { get; set; }

    /// <summary>The name on the sign-in button.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The issuer address.</summary>
    public string? Authority { get; set; }

    /// <summary>The client id.</summary>
    public string? ClientId { get; set; }

    /// <summary>The client secret; blank keeps the saved one. Never sent back.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Extra scopes, separated by spaces or semicolons.</summary>
    public string? Scopes { get; set; }

    /// <summary>Extra authorization request parameters, one <c>name=value</c> per line.</summary>
    public string? Parameters { get; set; }

    /// <summary>The claim holding the user name.</summary>
    public string? UserNameClaim { get; set; }

    /// <summary>The claim holding the email address.</summary>
    public string? EmailClaim { get; set; }

    /// <summary>The claim saying the address is verified.</summary>
    public string? EmailVerifiedClaim { get; set; }

    /// <summary>The claim that decides the administrator flag (the provider becomes authoritative for it).</summary>
    public string? AdministratorClaim { get; set; }

    /// <summary>The value of that claim that makes a user an administrator.</summary>
    public string? AdministratorValue { get; set; }

    /// <summary>The claim holding the user's groups.</summary>
    public string? GroupClaim { get; set; }

    /// <summary>Claim value to BaGet group, one <c>value=group</c> per line.</summary>
    public string? GroupMappings { get; set; }

    /// <summary>The administrator's password, asked again.</summary>
    public string? Password { get; set; }
}
