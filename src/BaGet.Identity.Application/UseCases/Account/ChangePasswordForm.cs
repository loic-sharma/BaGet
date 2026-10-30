namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>What the change-password form posts (the body of <c>POST /api/ui/account/settings/change-password</c>).</summary>
public sealed class ChangePasswordForm
{
    /// <summary>The current password (an account without one sets its first after signing in again with its provider).</summary>
    public string? CurrentPassword { get; set; }

    /// <summary>The new password.</summary>
    public string? NewPassword { get; set; }

    /// <summary>The new password again.</summary>
    public string? ConfirmPassword { get; set; }
}
