namespace BaGet.Identity.Application.Models.Settings;

/// <summary>A named OpenID Connect provider as the Settings screen lists it. The client secret is never carried, only whether one is set.</summary>
public sealed class SignInProviderView
{
    /// <summary>The provider id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The name on the sign-in button.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The issuer address.</summary>
    public string? Authority { get; set; }

    /// <summary>The client id.</summary>
    public string? ClientId { get; set; }

    /// <summary>Whether a client secret is set.</summary>
    public bool HasSecret { get; set; }

    /// <summary>Extra scopes.</summary>
    public string? Scopes { get; set; }

    /// <summary>Extra request parameters, one <c>name=value</c> per line.</summary>
    public string? Parameters { get; set; }

    /// <summary>The claim holding the user name.</summary>
    public string? UserNameClaim { get; set; }

    /// <summary>The claim holding the email address.</summary>
    public string? EmailClaim { get; set; }

    /// <summary>The claim saying the address is verified.</summary>
    public string? EmailVerifiedClaim { get; set; }

    /// <summary>The claim that decides the administrator flag.</summary>
    public string? AdministratorClaim { get; set; }

    /// <summary>The value of that claim that makes a user an administrator.</summary>
    public string? AdministratorValue { get; set; }

    /// <summary>The claim holding the user's groups.</summary>
    public string? GroupClaim { get; set; }

    /// <summary>Claim value to BaGet group, one <c>value=group</c> per line.</summary>
    public string? GroupMappings { get; set; }

    /// <summary>Whether the overrides file holds it, so this screen can edit or remove it; false when a configuration file or the environment sets it.</summary>
    public bool Editable { get; set; }

    /// <summary>Whether the running server has a handler for it (false: saved or removed since startup).</summary>
    public bool Active { get; set; }

    /// <summary>Whether what is saved differs from what the running server uses, so a restart is needed.</summary>
    public bool PendingRestart { get; set; }
}
