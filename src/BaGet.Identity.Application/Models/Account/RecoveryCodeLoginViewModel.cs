namespace BaGet.Identity.Application.Models.Account;

/// <summary>The second step of signing in with a recovery code. A request binds <see cref="RecoveryCodeLoginForm"/>, never this type.</summary>
public sealed class RecoveryCodeLoginViewModel : RecoveryCodeLoginForm
{
    /// <summary>The refusal to show.</summary>
    public string? Error { get; set; }
}
