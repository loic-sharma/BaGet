namespace BaGet.Identity.Application.Models.Account;

/// <summary>The second step of signing in: an authenticator code. A request binds <see cref="TwoFactorLoginForm"/>, never this type.</summary>
public sealed class TwoFactorLoginViewModel : TwoFactorLoginForm
{
    /// <summary>The refusal to show.</summary>
    public string? Error { get; set; }
}
