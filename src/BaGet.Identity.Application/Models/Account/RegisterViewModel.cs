namespace BaGet.Identity.Application.Models.Account;

/// <summary>The registration screen. A request binds <see cref="RegisterForm"/>, never this type; the passwords are always empty here.</summary>
public sealed class RegisterViewModel : RegisterForm
{
    /// <summary>Whether the account was created but waits for an administrator (<c>Accounts:RequireApproval</c>).</summary>
    public bool AwaitingApproval { get; set; }

    /// <summary>Whether a confirmation link was mailed.</summary>
    public bool ConfirmationSent { get; set; }

    /// <summary>Whether the screen asks for an email address.</summary>
    public bool EmailRequired { get; set; }

    /// <summary>Over <c>/api/ui</c> only: the tokens of the new account when it can sign in at once (no cookie is set for an HTTP caller). Null on the screen.</summary>
    public Session.SessionModel? Session { get; set; }
}
