namespace BaGet.Identity.Application.UseCases.Account;

/// <summary>What the change-email form posts.</summary>
public sealed class ChangeEmailForm
{
    /// <summary>The new address.</summary>
    public string? NewEmail { get; set; }

    /// <summary>The password, asked again (an account without one needs a fresh provider sign-in instead).</summary>
    public string? Password { get; set; }
}
