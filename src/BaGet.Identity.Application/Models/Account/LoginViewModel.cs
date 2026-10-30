namespace BaGet.Identity.Application.Models.Account;

/// <summary>
/// The sign-in screen: the form fields it repeats and what it shows. A request never binds this type (the form is <see cref="LoginForm"/>), so
/// the outputs cannot be set from outside; <see cref="LoginForm.Password"/> is always empty.
/// </summary>
public sealed class LoginViewModel : LoginForm
{
    /// <summary>Whether the registration link is shown.</summary>
    public bool AllowRegistration { get; set; }

    /// <summary>Whether sign-in with an external provider is offered (any provider in <see cref="ExternalProviders"/>).</summary>
    public bool HasExternalProvider { get; set; }

    /// <summary>The first provider's display name (kept for clients that offer one button); see <see cref="ExternalProviders"/>.</summary>
    public string? ExternalProviderName { get; set; }

    /// <summary>The configured external providers, one sign-in button each.</summary>
    public System.Collections.Generic.IReadOnlyList<ExternalProviderInfo> ExternalProviders { get; set; } = System.Array.Empty<ExternalProviderInfo>();

    /// <summary>Whether mail is configured (the forgot-password link is shown).</summary>
    public bool EmailIsConfigured { get; set; }

    /// <summary>The refusal to show.</summary>
    public string? Error { get; set; }

    /// <summary>The notice to show.</summary>
    public string? Notice { get; set; }
}
