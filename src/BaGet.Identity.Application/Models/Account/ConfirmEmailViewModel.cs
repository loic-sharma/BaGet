namespace BaGet.Identity.Application.Models.Account;

/// <summary>The result of following the link of a confirmation email.</summary>
public sealed class ConfirmEmailViewModel
{
    /// <summary>Whether the link was valid.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Whether the link changed the address of an existing account.</summary>
    public bool EmailChanged { get; set; }

    /// <summary>Whether the account still waits for an administrator (<c>Accounts:RequireApproval</c>) after the confirmation.</summary>
    public bool PendingApproval { get; set; }
}
