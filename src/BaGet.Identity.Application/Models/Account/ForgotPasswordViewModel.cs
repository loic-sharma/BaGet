namespace BaGet.Identity.Application.Models.Account;

/// <summary>The "forgot password" screen. A request binds <see cref="ForgotPasswordForm"/>, never this type.</summary>
public sealed class ForgotPasswordViewModel : ForgotPasswordForm
{
    /// <summary>Whether the request was answered (always the same answer, see the use case).</summary>
    public bool Sent { get; set; }
}
