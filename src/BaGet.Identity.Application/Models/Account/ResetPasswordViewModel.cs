namespace BaGet.Identity.Application.Models.Account;

/// <summary>The screen behind the link of a password reset email. A request binds <see cref="ResetPasswordForm"/>, never this type.</summary>
public sealed class ResetPasswordViewModel : ResetPasswordForm
{
    /// <summary>The refusal to show.</summary>
    public string? Error { get; set; }
}
